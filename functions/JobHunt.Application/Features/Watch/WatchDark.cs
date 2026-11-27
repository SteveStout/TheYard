using JobHunt.Domain.Features.Watch;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Tracker;

namespace JobHunt.Application.Features.Watch;

/// <summary>
/// The dark-lane alarm's write for one tick, in shadow and on alike (it tells, it heals nothing): reads what
/// <see cref="DarkLanes"/> judges, then rewrites NOT-WORKING.md whole (a temp file replaced) with today's DARK lines at
/// the top, under its title, and every other line as it was. A NOT WORKING line for a dark lane loses its "Needs you:
/// nothing.", which read as routine while the lane sent nothing (audit1124b F2), and says what is fixing it instead.
/// Nothing is written when the file would not change.
/// </summary>
/// <param name="store">The share.</param>
public sealed class WatchDark(IMergeStore store)
{
    /// <summary>The file the card's first lines are read from.</summary>
    public const string Path = "dispatch/NOT-WORKING.md";

    /// <summary>A landed fix's line while it waits on its proof run; its lane is not dark until the run reads empty.</summary>
    public const string WaitingPath = "dispatch/NOT-WORKING-waiting.md";

    private const string RulesLine = "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).";
    private const string Title = "# NOT WORKING: one line a failure the heal ladder could not fix (the source of the card's first lines)";

    /// <summary>Writes today's DARK lines; answers them.</summary>
    /// <param name="now">The tick's time.</param>
    /// <param name="ct">Stops the reads and the write.</param>
    public async Task<IReadOnlyList<string>> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        var reads = await ReadAsync(today, ct);
        var dark = DarkLanes.Lines(reads, today);
        string before = await TextAsync(Path, ct);
        string after = Render(before, dark);
        if (after != before)
        {
            await store.WriteAsync(MergePaths.Temp(Path), TrackerCsvText.Encode(after), ct);
            await store.ReplaceAsync(MergePaths.Temp(Path), Path, ct);
        }
        return dark;
    }

    /// <summary>The file with today's DARK lines on top and every other line kept; a dark lane's NOT WORKING line says its fix.</summary>
    /// <param name="before">NOT-WORKING.md as it stands, or empty.</param>
    /// <param name="dark">Today's DARK lines.</param>
    public static string Render(string before, IReadOnlyList<string> dark)
    {
        var kept = (before ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => !l.StartsWith(DarkLanes.Prefix, StringComparison.Ordinal) && l != RulesLine && !l.StartsWith("# NOT WORKING", StringComparison.Ordinal))
            .SkipWhile(l => l.Length == 0).Select(l => Fixed(l, dark)).ToList();
        while (kept.Count > 0 && kept[^1].Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }
        var lines = new List<string> { RulesLine, string.Empty, Title, string.Empty };
        lines.AddRange(dark);
        if (dark.Count > 0 && kept.Count > 0)
        {
            lines.Add(string.Empty);
        }
        lines.AddRange(kept);
        return string.Join("\n", lines) + "\n";
    }

    private static string Fixed(string line, IReadOnlyList<string> dark)
    {
        const string nothing = "Needs you: nothing.";
        if (!line.StartsWith("NOT WORKING: ", StringComparison.Ordinal) || !line.EndsWith(nothing, StringComparison.Ordinal))
        {
            return line;
        }
        var lane = DarkLanes.Lanes.FirstOrDefault(l => line.StartsWith($"NOT WORKING: {l.Key} since ", StringComparison.Ordinal));
        string? mine = lane is null ? null : dark.FirstOrDefault(d => d.StartsWith($"{DarkLanes.Prefix}{lane.Label}:", StringComparison.Ordinal));
        return mine is null ? line : line[..^nothing.Length] + "Fix: " + mine[(mine.IndexOf(" Fix: ", StringComparison.Ordinal) + 6)..];
    }

    private async Task<DarkReads> ReadAsync(DateOnly today, CancellationToken ct)
    {
        string state = await TextAsync("state.md", ct);
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in DarkLanes.NotesWanted(state, today))
        {
            notes[path] = await TextAsync(path, ct);
        }
        var holds = new List<string>();
        foreach (var file in (await store.ListFilesAsync("dispatch/held", ct)).Where(f => f.Name.EndsWith(".hold", StringComparison.Ordinal)))
        {
            holds.Add(await TextAsync($"dispatch/held/{file.Name}", ct));
        }
        // A running item is its folder; a heal's folder drops the "30-" its ready file carried.
        var queued = new HashSet<string>((await store.ListFilesAsync("dispatch/ready", ct)).Select(f => f.Name)
            .Concat((await store.ListFoldersAsync("dispatch/running", ct)).SelectMany(f => new[] { f + ".md", $"30-{f}.md" })), StringComparer.Ordinal);
        var proposed = new HashSet<string>((await store.ListFilesAsync("dispatch/proposed", ct)).Select(f => f.Name), StringComparer.Ordinal);
        return new DarkReads(state, notes, holds, await TextAsync("dispatch/EMPTY-RUNS.md", ct), await TextAsync(MergePaths.Master, ct),
            await TextAsync("campaign/pass.log", ct), queued, proposed, await TextAsync(DarkLanes.SignInPath, ct), await TextAsync(WaitingPath, ct));
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;
}
