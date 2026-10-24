// The Claude usage card's four questions (ADR: Every Claude lane reports its usage): who used
// what, what each run cost in tokens, whether the plan is near its limit, and how the runs sit
// against the nightly Function's slots. Its own file so the card's file holds the dials, the chart
// and the readout, and each stays short.
using System.Globalization;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The Claude usage card's rows, and the words a run is printed in.</summary>
public static class ClaudeRows
{
    /// <summary>The run table's columns.</summary>
    public static readonly IReadOnlyList<string> Columns = ["When (Central)", "Slot", "Mode", "Model", "In", "Cache read", "Out", "Wall", "Cost", "Run"];

    /// <summary>The most runs the table lists, newest first.</summary>
    public const int TableRows = 50;

    /// <summary>What the slot row says of a nightly run with no usage document.</summary>
    public const string NoUsage = "no usage reported";

    /// <summary>What the slot row says of a nightly lane's usage document whose run the nightly reader never listed.</summary>
    public const string NotSeen = "run not seen by the nightly reader";

    /// <summary>The slot a run belongs to: the hour of its HH:MM, at the top of the hour, as the Function's slots are.</summary>
    /// <param name="slot">The run's HH:MM; null for none.</param>
    public static string SlotHour(string? slot) => slot is { Length: >= 2 } s ? s[..2] + ":00" : "no slot";

    /// <summary>A run's mode, with a dry run said in words.</summary>
    /// <param name="record">The run.</param>
    public static string ModeWords(UsageRecord record) =>
        record.Mode is not { } mode ? "no mode" : record.Dry ? mode[..^"-dry".Length] + " (dry run)" : mode;

    /// <summary>A run in the readout's words: when, slot, mode, model, wall, the counts and the cost.</summary>
    /// <param name="record">The run.</param>
    public static string RunWords(UsageRecord record) =>
        $"{NightlySlots.Local(record.At).ToString("MMM d HH:mm", CultureInfo.InvariantCulture)} Central, {record.Slot ?? "no slot"}, {ModeWords(record)}, {record.Model}, "
        + $"{NightlyRows.Took(record.WallMs / 1000)}, in {ClaudeCard.Tokens(record.TokensIn)} / cache read {ClaudeCard.Tokens(record.CacheRead)} / out {ClaudeCard.Tokens(record.TokensOut)}, {ClaudeCard.Cost(record.CostUsd)}";

    /// <summary>A run's GitHub link when its id is a GitHub run id and the repository is known.</summary>
    /// <param name="record">The run.</param>
    /// <param name="repo">The repository, owner/name; null for none.</param>
    public static string? RunLink(UsageRecord record, string? repo) =>
        repo is not null && record.Run.All(char.IsAsciiDigit) ? $"https://github.com/{repo}/actions/runs/{record.Run}" : null;

    /// <summary>The window's runs as the row's table, newest first, at most fifty.</summary>
    /// <param name="records">The window's documents, newest first.</param>
    /// <param name="repo">The repository the nightly workflow runs in; null for none.</param>
    public static CloudTable RunTable(IReadOnlyList<UsageRecord> records, string? repo) =>
        records.Count == 0 ? CloudTable.None : new CloudTable(Columns, [.. records.Take(TableRows).Select(record => (IReadOnlyList<CloudLink>)
        [
            new(NightlySlots.Local(record.At).ToString("MMM d HH:mm", CultureInfo.InvariantCulture), null),
            new(record.Slot ?? "", null),
            new(ModeWords(record), null),
            new(record.Model, null),
            new(ClaudeCard.Tokens(record.TokensIn), null),
            new(ClaudeCard.Tokens(record.CacheRead), null),
            new(ClaudeCard.Tokens(record.TokensOut), null),
            new(NightlyRows.Took(record.WallMs / 1000), null),
            new(record.CostUsd is { } d ? "$" + d.ToString("0.00", CultureInfo.InvariantCulture) : "", null),
            new(record.Run, RunLink(record, repo)),
        ])]);

    /// <summary>
    /// The nightly lane's runs joined to the nightly reader's by GitHub run id, slot by slot: what each
    /// slot's runs cost, every run the reader listed with no usage document, and every usage document
    /// whose run the reader never listed.
    /// </summary>
    /// <param name="records">The window's documents.</param>
    /// <param name="runs">The nightly reader's runs in the window.</param>
    public static (IReadOnlyList<string> Lines, int Matched, int Missing, int Unseen) SlotJoin(IReadOnlyList<UsageRecord> records, IReadOnlyList<NightlyRun> runs)
    {
        var nightly = records.Where(record => record.Lane == ClaudeCard.NightlyLane).ToList();
        var reported = nightly.Select(record => record.Run).ToHashSet(StringComparer.Ordinal);
        var listed = runs.Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        var lines = nightly
            .GroupBy(record => SlotHour(record.Slot), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {ClaudeCard.Totals([.. group])}")
            .ToList();
        var missing = runs.Where(run => !reported.Contains(run.Id)).ToList();
        var unseen = nightly.Where(record => !listed.Contains(record.Run)).ToList();
        lines.AddRange(missing.Take(5).Select(run => $"run #{run.Number} ({SlotHour(run.Slot)}, {run.Lane}): {NoUsage}"));
        lines.AddRange(unseen.Take(5).Select(record => $"run {record.Run} ({SlotHour(record.Slot)}): {NotSeen}"));
        return (lines, runs.Count - missing.Count, missing.Count, unseen.Count);
    }

    /// <summary>The four rows.</summary>
    /// <param name="records">The window's documents, newest first.</param>
    /// <param name="runs">The nightly reader's runs in the window.</param>
    /// <param name="repo">The repository the nightly workflow runs in; null for none.</param>
    public static IReadOnlyList<CloudRow> Rows(IReadOnlyList<UsageRecord> records, IReadOnlyList<NightlyRun> runs, string? repo)
    {
        var lanes = records.OrderBy(record => record.At).GroupBy(record => record.Lane, StringComparer.Ordinal).ToList();
        var counted = records.Where(record => !record.Dry).ToList();
        var longest = counted.Count == 0 ? null : counted.MaxBy(record => record.WallMs);
        bool slow = longest is not null && longest.WallMs / 1000.0 > CloudThresholds.ClaudeRunSeconds;
        var (lines, matched, missing, unseen) = SlotJoin(records, runs);
        return
        [
            new("lanes", "Who used what?",
                records.Count == 0 ? "no run reported in the window" : $"{lanes.Count} {(lanes.Count == 1 ? "lane" : "lanes")}: {ClaudeCard.Totals(records)}",
                [.. lanes.Select(lane => new CloudLink($"{lane.Key} ({(lane.First().Kind == UsageKind.Api ? "billed per token" : "billed to the plan")}): {ClaudeCard.Totals([.. lane])}", null))],
                "info")
            { Source = ClaudeCard.Documents },
            new("runs", "What did the runs cost in tokens?",
                records.Count == 0 ? "no run reported in the window" : $"{records.Count} {(records.Count == 1 ? "run" : "runs")}, the longest {NightlyRows.Took((longest?.WallMs ?? 0) / 1000)}",
                [new($"a run over {CloudThresholds.ClaudeRunSeconds.ToString("N0", CultureInfo.InvariantCulture)} s of wall is a warning · dry runs are listed and left out of the averages", null),
                    new("cost is the API-equivalent price of a run billed to the plan, never a bill", null)],
                records.Count == 0 ? "info" : slow ? "warning" : "good")
            { Table = RunTable(records, repo), Source = ClaudeCard.Documents },
            new("plan", "Is it near the plan's limit?", ClaudeCard.PlanUnread,
                [new("the plan's own bars (the session, the week across models) are read once the plan's usage endpoint is set", null)],
                "info")
            { Source = "the plan's usage endpoint, not set" },
            new("function", "How does it relate to the Function?",
                runs.Count == 0 && lines.Count == 0 ? "no nightly run in the window"
                    : $"{matched} of {runs.Count} nightly runs reported usage" + (unseen > 0 ? $", {unseen} not seen by the nightly reader" : ""),
                [.. lines.Select(line => new CloudLink(line, null))],
                runs.Count == 0 ? "info" : missing == 0 && unseen == 0 ? "good" : "info")
            { Source = "the usage documents joined to the nightly card's run log by GitHub run id" },
        ];
    }
}
