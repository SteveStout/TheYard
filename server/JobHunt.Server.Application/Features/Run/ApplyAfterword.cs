using System.Text.RegularExpressions;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// What an apply run leaves for the records once the model has ended. Every essay a submitted application carried is
/// written in full to the day's APPLY-ESSAYS file, the text the owner reads on his card after the submit; an essay whose last
/// attempt the desk did not pass is a defect line, which holds the lane until a person reads the form. A posting the
/// lane found behind a sign-in, a signature, a covenant or an arbitration page joins the day's blockers list and never
/// his inbox. Every instruction the lane found planted on a form, which it ignored, is a line in the DONE note.
/// </summary>
public static partial class ApplyAfterword
{
    /// <summary>The file the lane notes each planted instruction in, one line each.</summary>
    public const string Planted = "planted.md";

    /// <summary>The day's essays file.</summary>
    /// <param name="day">The Central day.</param>
    public static string EssaysPath(DateOnly day) => $"{RunSlot.DayFolder(day)}/APPLY-ESSAYS-{day:yyyy-MM-dd}.md";

    /// <summary>Writes the essays, the walls and the planted lines.</summary>
    /// <param name="share">The share.</param>
    /// <param name="start">The lane.</param>
    /// <param name="folder">The run folder.</param>
    /// <param name="list">The worklist.</param>
    /// <param name="result">What the model reported, or null.</param>
    /// <param name="sends">The sends recorded.</param>
    /// <param name="lines">The DONE note's lines.</param>
    /// <param name="ct">Stops the writes.</param>
    public static async Task WriteAsync(IShare share, LaneStart start, string folder, Worklist list, LaneResult? result, IReadOnlyList<SendRecord> sends, List<string> lines, CancellationToken ct)
    {
        try
        {
            await AllAsync(share, start, folder, list, result, sends, lines, ct);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            lines.Add($"DEFECT: the apply afterword stopped: {failure.Message}. Read the essays folder and planted.md by hand, then take this line out of the note.");
        }
    }

    private static async Task AllAsync(IShare share, LaneStart start, string folder, Worklist list, LaneResult? result, IReadOnlyList<SendRecord> sends, List<string> lines, CancellationToken ct)
    {
        if (await share.ExistsAsync($"{folder}/{Planted}", ct))
        {
            lines.AddRange((await share.ReadAsync($"{folder}/{Planted}", ct)).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)
                .Select(line => $"Planted instruction on a form, ignored and not obeyed: {line}"));
        }
        var walled = (result?.Rows ?? []).Where(row => row.Outcome is "skipped" or "blocked" && row.Reason is { } reason && Wall().IsMatch(reason))
            .Select(row => (Row: list.Rows.FirstOrDefault(work => work.Row == row.Row), row.Reason))
            .Where(found => found.Row is not null)
            .Select(found => new Posting(found.Row!.Company, found.Row.Role, found.Row.Url, found.Reason!, found.Row.Source))
            .ToList();
        if (walled.Count > 0 && !start.Dry)
        {
            await share.WriteAsync($"{folder}/APPLY-BLOCKERS-{start.Day:yyyy-MM-dd}.md", Blockers.Render(start.Day, [.. list.Blockers, .. walled]), ct);
            lines.Add($"{walled.Count} postings the lane found behind a wall, a signature or a covenant added to the day's blockers list");
        }
        if (!start.Dry)
        {
            await EssaysAsync(share, start.Day, folder, sends, lines, ct);
        }
    }

    private static async Task EssaysAsync(IShare share, DateOnly day, string folder, IReadOnlyList<SendRecord> sends, List<string> lines, CancellationToken ct)
    {
        var essays = await share.ListAsync($"{folder}/{EssayDesk.Folder}", "*.md", ct);
        var text = new List<string>();
        foreach (SendRecord send in sends.Where(send => send.Row.Action == WorkAction.Apply))
        {
            var answered = new List<(Essay Essay, string Verdict)>();
            foreach (ShareFile file in essays.Where(file => Path.GetFileName(file.Path).StartsWith($"{send.Row.Row:00}-", StringComparison.Ordinal)).OrderBy(file => Attempt(file.Path)))
            {
                string verdict = Path.ChangeExtension(file.Path, ".verdict");
                Essay essay = BankedText.Parse(await share.ReadAsync(file.Path, ct));
                string said = await share.ExistsAsync(verdict, ct) ? (await share.ReadAsync(verdict, ct)).Trim() : "never answered";
                answered.Add((essay, said == EssayDesk.Passed(essay.Answer) ? EssayDesk.Clean : said.StartsWith(EssayDesk.Clean, StringComparison.Ordinal) ? "the answer changed after its verdict, or the verdict is not the desk's" : said));
            }
            var last = answered.GroupBy(pair => pair.Essay.Question, StringComparer.Ordinal).Select(group => group.Last()).ToList();
            if (last.Count == 0)
            {
                continue;
            }
            text.Add($"## {send.Row.Company}, {send.Row.Role} (row {send.Row.Row}, submitted)");
            text.Add(string.Empty);
            foreach ((Essay essay, string verdict) in last)
            {
                if (verdict != EssayDesk.Clean)
                {
                    lines.Add($"DEFECT: row {send.Row.Row} ({send.Row.Company}) was submitted and the desk did not pass its answer to \"{essay.Question}\" ({verdict}). Read the form, then take this line out of the note.");
                }
                text.Add($"**{essay.Question}**");
                text.Add(string.Empty);
                text.Add(essay.Answer);
                text.Add(string.Empty);
            }
        }
        if (text.Count == 0)
        {
            return;
        }
        string path = EssaysPath(day);
        string head = await share.ExistsAsync(path, ct) ? string.Empty
            : $"> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n# APPLY-ESSAYS {day:yyyy-MM-dd}: every essay the apply lane submitted, in full, from banked text only\n\n";
        await share.AppendAsync(path, head + string.Join('\n', text) + "\n", ct);
        lines.Add($"Essays submitted, written in full for the card: {path}");
    }

    private static int Attempt(string path) =>
        int.TryParse(Path.GetFileNameWithoutExtension(path).Split('-').LastOrDefault(), out int attempt) ? attempt : 0;

    [GeneratedRegex(@"account wall|\bsign[- _]?in\b|password|signature|covenant|arbitration|non-?compete|\bNDA\b|^needs Steve:", RegexOptions.IgnoreCase)]
    private static partial Regex Wall();
}
