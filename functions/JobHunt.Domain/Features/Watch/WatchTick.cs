using System.Globalization;
using System.Text;

namespace JobHunt.Domain.Features.Watch;

/// <summary>How the watch acts, from the WATCH switch: shadow writes WATCH.md and heals nothing, on runs the heal ladder.</summary>
public enum WatchMode
{
    /// <summary>Anything but shadow or on: the tick does nothing.</summary>
    Off,

    /// <summary>The tick reads every test and writes WATCH.md; it heals nothing.</summary>
    Shadow,

    /// <summary>The tick reads every test, writes WATCH.md and starts the heal ladder for a failure.</summary>
    On,
}

/// <summary>One test's reading in one tick.</summary>
/// <param name="Test">Its number in the plan's table, "W2".</param>
/// <param name="Name">What it tests, in a few words.</param>
/// <param name="Pass">True passed, false failed, null not read (its source would not answer).</param>
/// <param name="Number">The number it read, as text: "dry", "3 of 3", "12 min".</param>
/// <param name="Source">Where the number was read: the real source the plan names, never a lane's own report.</param>
public sealed record WatchResult(string Test, string Name, bool? Pass, string Number, string Source)
{
    /// <summary>The word WATCH.md and the change log print.</summary>
    public string Word => Pass switch { true => "pass", false => "FAIL", null => "not read" };
}

/// <summary>
/// The watch's two files (the year-end plan's 7a and answer 6 of its build prompt): WATCH.md, the newest tick written
/// whole, one line a test; and a change log, dispatch/watch/yyyy-MM-dd.tsv, that gets a line only when a test's word
/// changes, so the share is not walked 96 times a day.
/// </summary>
public static class WatchTick
{
    /// <summary>WATCH.md on the share.</summary>
    public const string File = "dispatch/WATCH.md";

    /// <summary>The setting that picks the mode.</summary>
    public const string Setting = "WATCH";

    /// <summary>The mode a WATCH value names: shadow, on, or off for anything else.</summary>
    /// <param name="value">The setting's value.</param>
    public static WatchMode Mode(string? value) =>
        string.Equals(value?.Trim(), "on", StringComparison.OrdinalIgnoreCase) ? WatchMode.On
        : string.Equals(value?.Trim(), "shadow", StringComparison.OrdinalIgnoreCase) ? WatchMode.Shadow
        : WatchMode.Off;

    /// <summary>The day's change log on the share.</summary>
    /// <param name="day">The Central day.</param>
    public static string ChangeLog(DateOnly day) => $"dispatch/watch/{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.tsv";

    /// <summary>WATCH.md for one tick: its time, its mode, then one line a test in the plan's order.</summary>
    /// <param name="local">The tick's Central time.</param>
    /// <param name="mode">The mode it ran in.</param>
    /// <param name="results">The readings.</param>
    public static string Render(DateTime local, WatchMode mode, IReadOnlyList<WatchResult> results)
    {
        var text = new StringBuilder();
        text.Append("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n");
        text.Append("# WATCH: the newest tick, written whole by the job-hunt Function's watch\n\n");
        text.Append(CultureInfo.InvariantCulture, $"Tick {local:yyyy-MM-dd HH:mm} Central, mode {mode.ToString().ToLowerInvariant()}: {results.Count(r => r.Pass == true)} pass, {results.Count(r => r.Pass == false)} fail, {results.Count(r => r.Pass is null)} not read.\n\n");
        text.Append("| Time | Test | What | Result | Read | Source |\n|---|---|---|---|---|---|\n");
        foreach (WatchResult result in results.OrderBy(r => Order(r.Test)).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"| {local:HH:mm} | {result.Test} | {Cell(result.Name)} | {result.Word} | {Cell(result.Number)} | {Cell(result.Source)} |\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// The change log's new lines: one for each test whose word differs from the last word the log holds for it (a
    /// test the log has never seen counts as changed). Tab-separated: time, test, name, word, number.
    /// </summary>
    /// <param name="local">The tick's Central time.</param>
    /// <param name="log">The day's change log as it stands, empty when there is none.</param>
    /// <param name="results">The readings.</param>
    public static string Changes(DateTime local, string log, IReadOnlyList<WatchResult> results)
    {
        var last = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in (log ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length >= 4)
            {
                last[cells[1] + "\t" + cells[2]] = cells[3];
            }
        }
        var added = new StringBuilder();
        foreach (WatchResult result in results)
        {
            string key = result.Test + "\t" + Tab(result.Name);
            if (!last.TryGetValue(key, out string? word) || word != result.Word)
            {
                added.Append(CultureInfo.InvariantCulture, $"{local:HH:mm}\t{key}\t{result.Word}\t{Tab(result.Number)}\n");
            }
        }
        return added.ToString();
    }

    private static int Order(string test) => int.TryParse(test.TrimStart('W'), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 99;

    private static string Cell(string text) => (text ?? string.Empty).Replace("|", "/", StringComparison.Ordinal).Replace('\n', ' ');

    private static string Tab(string text) => (text ?? string.Empty).Replace('\t', ' ').Replace('\n', ' ');
}
