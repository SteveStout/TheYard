using System.Text.RegularExpressions;

namespace JobHunt.Infrastructure.Features.Card;

/// <summary>
/// Reads what the morning card is drawn from: the newest real reply run's queue of the day and every draft it or the
/// carried rejection reads name (with the DO-NOT-SEND note and the .gate.md beside each), the dispatcher's STATUS.md,
/// LEFT-FOR-STEVE.md, ANSWERED.md, WALLS.md and DECISION.md, the day's APPLY-ESSAYS file, TODAY.md as it stands, and
/// the next 72 hours of the calendar with each call's must-answers line from its company's NOTES file. A file that is
/// not there reads as empty; the card says so where it matters.
/// </summary>
public sealed partial class TodayCardSource(IShareReader store, IHttpClientFactory http, TimeProvider time)
{
    /// <summary>The dispatcher's folder on the share.</summary>
    public const string Dispatch = "dispatch";

    /// <summary>Every input the card draws.</summary>
    /// <param name="ct">Cancels the reads.</param>
    public async Task<TodayInputs> ReadAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var local = CentralClock.Local(now);
        string day = $"nightly/{local:yyyy-MM-dd}";
        var runs = new List<(string, bool)>();
        foreach (string folder in await store.ListFoldersAsync(day, ct))
        {
            if (ReplyRetry.IsRun(folder))
            {
                runs.Add((folder, await store.ExistsAsync($"{day}/{folder}/{ReplyRetry.Queue}", ct)));
            }
        }
        string? run = TodayParts.NewestRun(runs);
        string? queuePath = run is null ? null : $"{day}/{run}/{ReplyRetry.Queue}";
        string queue = queuePath is null ? string.Empty : await TextAsync(queuePath, ct);
        string previous = await TextAsync(TodayCard.Path, ct);
        var drafts = new Dictionary<string, string>(StringComparer.Ordinal);
        var doNotSend = new HashSet<string>(StringComparer.Ordinal);
        var gates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in TodayCard.DraftPaths(queue, previous))
        {
            string stem = path.Contains('.', StringComparison.Ordinal) ? path[..path.LastIndexOf('.')] : path;
            if (await store.ExistsAsync(stem + ".DO-NOT-SEND.txt", ct))
            {
                doNotSend.Add(path);
            }
            if (await store.ExistsAsync(path, ct))
            {
                drafts[path] = await TextAsync(path, ct);
            }
            string gate = await TextAsync(stem + ".gate.md", ct);
            if (TodayParts.Lines(gate).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('>') && !l.StartsWith('#')) is { } first)
            {
                gates[path] = first.TrimStart('-', ' ');
            }
        }
        string essaysPath = $"{day}/APPLY-ESSAYS-{local:yyyy-MM-dd}.md";
        bool essays = await store.ExistsAsync(essaysPath, ct);
        var calls = await CallsAsync(now, ct);
        return new TodayInputs(
            local,
            queuePath,
            queue,
            drafts,
            doNotSend,
            gates,
            await TextAsync($"{Dispatch}/STATUS.md", ct),
            await TextAsync($"{Dispatch}/LEFT-FOR-STEVE.md", ct),
            await TextAsync($"{Dispatch}/ANSWERED.md", ct),
            await TextAsync($"{Dispatch}/WALLS.md", ct),
            await TextAsync($"{Dispatch}/DECISION.md", ct),
            essays ? essaysPath : null,
            essays ? await TextAsync(essaysPath, ct) : string.Empty,
            previous,
            calls);
    }

    /// <summary>
    /// The must-answers line for a call: the first "UNANSWERED IN WRITING" or "NOT IN WRITING" line of the NOTES file
    /// whose company the call's line names, or which file holds none, or that no NOTES file names the company.
    /// </summary>
    /// <param name="call">The call's card line.</param>
    /// <param name="notes">Each NOTES file's name with its text.</param>
    public static string MustAnswers(string call, IReadOnlyList<(string Name, string Text)> notes)
    {
        string letters = Letters().Replace(call.ToUpperInvariant(), string.Empty);
        foreach (var (name, text) in notes)
        {
            string key = name["NOTES-".Length..^".md".Length].Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            if (key.Length < 3 || !letters.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }
            string? open = TodayParts.Lines(text).Select(l => l.Trim()).FirstOrDefault(l => l.Contains("NOT IN WRITING", StringComparison.Ordinal) || l.Contains("UNANSWERED IN WRITING", StringComparison.Ordinal));
            return open is null
                ? $"Must-answers: {name} holds no UNANSWERED IN WRITING card; read it before the call."
                : $"Must-answers: {(open.Length > 300 ? open[..300] + "..." : open)} ({name})";
        }
        return "Must-answers: no NOTES file names this company, so none is on record in writing.";
    }

    private async Task<IReadOnlyList<string>> CallsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var lines = await CalendarWeek.ReadAsync(http, now, 3, "No interview or call on the calendar in the next 72 hours (Google Calendar, primary).", ct);
        if (!lines.Any(l => l.EndsWith("(Google Calendar)", StringComparison.Ordinal)))
        {
            return lines;
        }
        var notes = new List<(string, string)>();
        foreach (var file in await store.ListFilesAsync(string.Empty, ct))
        {
            if (file.Name.StartsWith("NOTES-", StringComparison.Ordinal) && file.Name.EndsWith(".md", StringComparison.Ordinal))
            {
                notes.Add((file.Name, await TextAsync(file.Name, ct)));
            }
        }
        return [.. lines.Select(l => l.EndsWith("(Google Calendar)", StringComparison.Ordinal) ? $"{l}. {MustAnswers(l, notes)}" : l)];
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;

    [GeneratedRegex("[^A-Z]")]
    private static partial Regex Letters();
}
