using System.Globalization;
using System.Text;

namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>
/// The morning card, six lines or fewer: what went out per lane, what is held and why, what is blocked with its one fix.
/// It carries no question, because a card that has to be answered is an interruption. Extra lines past the sixth are
/// folded into the sixth, never dropped: an unwritten tracker row must reach the card however many there are.
/// </summary>
public static class RunCard
{
    /// <summary>The most lines a card may hold.</summary>
    public const int MostLines = 6;

    /// <summary>The card.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="reports">One report per lane.</param>
    /// <param name="extra">Blockers and eligibility lines, most urgent first.</param>
    public static string Render(DateOnly day, bool dry, IReadOnlyList<LaneReport> reports, IReadOnlyList<string> extra)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{RegistryRows.Lane} {day:ddd M/d}{(dry ? " DRY RUN, nothing sent" : string.Empty)}:"),
        };
        lines.AddRange(reports.Select(Line));
        int room = MostLines - lines.Count;
        lines.AddRange(extra.Take(Math.Max(0, Math.Min(extra.Count, room) - (extra.Count > room ? 1 : 0))));
        if (extra.Count > room && room > 0)
        {
            lines.Add(string.Join(" | ", extra.Skip(room - 1)));
        }
        return string.Join('\n', lines.Take(MostLines).Select(line => line.Replace("?", string.Empty, StringComparison.Ordinal))) + "\n";
    }

    private static string Line(LaneReport report)
    {
        string word = RunSlot.Word(report.Lane);
        if (report.Held is not null)
        {
            return $"{word}: held, {report.Held}";
        }
        if (report.Lane == Lane.Feed)
        {
            return $"{word}: {report.Queued} queued{(report.Dry ? " in its run folder only" : string.Empty)}, {report.Dropped.Count} left out by the gates{(report.CutShort ? ", cut short, read its DONE note" : string.Empty)}";
        }
        if (report.Dry)
        {
            int read = report.Answer?.Rows.Count ?? 0;
            return $"{word}: dry, {read} of {report.Rows.Count} rows read, {report.Dropped.Count} left out by the gates";
        }
        var line = new StringBuilder($"{word}: {report.Verified} sent verified of {report.Rows.Count}");
        int claimed = report.Sends.Count - report.Verified;
        if (claimed > 0)
        {
            line.Append($", {claimed} UNVERIFIED, read the page");
        }
        if (report.Answer?.LiveInviteCount is { } live)
        {
            line.Append($", {live + report.Sends.Count} invites in 7 days");
        }
        if (report.NoRecount)
        {
            line.Append(", DEFECT: no live count read first");
        }
        if (report.Answer is { StopCondition: not "none" } stopped)
        {
            line.Append($", STOPPED: {stopped.StopCondition}");
        }
        else if (report.Answer is null && report.Rows.Count > 0)
        {
            line.Append(", no result from the model, read DONE.md");
        }
        if (report.CutShort && report.Answer is not null)
        {
            line.Append(", cut short, read DONE.md");
        }
        return line.Append($", {report.Dropped.Count} left out").ToString();
    }
}

/// <summary>
/// The clean-run table, nightly/server6am-runs.tsv: one line per lane per live run with a send in it. A lane is eligible
/// for its target model after five clean runs in a row. A run that sent nothing proves nothing, so it adds no line.
/// Switching is a line in the local config and is a person's call; the program only says when.
/// </summary>
public static class CleanRuns
{
    /// <summary>Clean runs in a row a lane needs.</summary>
    public const int Needed = 5;

    /// <summary>The table's line for a lane's run, or null for a run that adds none.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="report">The lane's report.</param>
    public static string? Line(DateOnly day, LaneReport report) =>
        report.Dry || report.Sends.Count == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{day:yyyy-MM-dd}\t{RunSlot.Word(report.Lane)}\t{report.Model}\t{(report.Clean ? "clean" : "defect")}\t{report.Sends.Count}\n");

    /// <summary>True when the newest <see cref="Needed"/> lines for a lane on its current model are all clean.</summary>
    /// <param name="table">The table's text.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="model">The model the lane runs on now.</param>
    public static bool Eligible(string table, Lane lane, string model)
    {
        string[] states = [.. table.Split('\n')
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 4 && cells[1] == RunSlot.Word(lane) && cells[2] == model)
            .Select(cells => cells[3])];
        return states.Length >= Needed && states[^Needed..].All(state => state == "clean");
    }
}
