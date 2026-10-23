// The Claude usage card (ADR: Every Claude lane reports its usage): today's tokens out against the
// seven days before it and the plan's week on the dials, tokens a day per lane on the chart, four
// questions, and a readout of the last run. Every figure is read from the usage documents the lanes
// post; the lanes come from the documents, never from a list here. A dry run is shown and left out
// of every average, and every dollar is the API-equivalent price of a run billed to the plan.
using System.Globalization;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The Claude usage card.</summary>
public static class ClaudeCard
{
    /// <summary>The lane the nightly workflow posts under, which the Function's slots are joined on.</summary>
    public const string NightlyLane = "Azure Functions, nightly lane";

    /// <summary>What the plan dial and row say until the plan's own usage is read.</summary>
    public const string PlanUnread = "not read: the plan endpoint is not set";

    /// <summary>Where the documents are read, for every row and line that reads them.</summary>
    public const string Documents = "the usage documents the lanes post";

    /// <summary>The documents inside the card's window, newest first.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="records">The documents read, newest first.</param>
    public static IReadOnlyList<UsageRecord> InWindow(ResourceCardInputs inputs, IReadOnlyList<UsageRecord> records) =>
        [.. records.Where(record => record.At >= inputs.Now - inputs.Length && record.At <= inputs.Now)];

    /// <summary>Tokens out since the Central day began, dry runs left out.</summary>
    /// <param name="records">The documents.</param>
    /// <param name="now">When the card is drawn.</param>
    public static long Today(IReadOnlyList<UsageRecord> records, DateTimeOffset now)
    {
        var start = NightlySlots.StartOfCentralDay(now);
        return records.Where(record => !record.Dry && record.At >= start && record.At <= now).Sum(record => record.TokensOut);
    }

    /// <summary>The seven Central days before today, their tokens out a day on average, dry runs left out; null when they hold none.</summary>
    /// <param name="records">The documents.</param>
    /// <param name="now">When the card is drawn.</param>
    public static double? WeekAverage(IReadOnlyList<UsageRecord> records, DateTimeOffset now)
    {
        var today = NightlySlots.StartOfCentralDay(now);
        // Two hours past seven days back is inside the right Central day whatever daylight saving did.
        var from = NightlySlots.StartOfCentralDay(today.AddDays(-7).AddHours(2));
        var week = records.Where(record => !record.Dry && record.At >= from && record.At < today).ToList();
        return week.Count == 0 ? null : week.Sum(record => record.TokensOut) / 7d;
    }

    /// <summary>Today against the week's average, in per cent; null when there is no average.</summary>
    /// <param name="records">The documents.</param>
    /// <param name="now">When the card is drawn.</param>
    public static double? TodayShare(IReadOnlyList<UsageRecord> records, DateTimeOffset now) =>
        WeekAverage(records, now) is { } average && average > 0 ? Math.Round(Today(records, now) / average * 100, 1) : null;

    /// <summary>A tone from a share against a card's two lines.</summary>
    /// <param name="share">The share; null reads as information.</param>
    /// <param name="lines">The amber and red lines.</param>
    public static string Tone(double? share, (double Amber, double Red) lines) =>
        share is not { } s ? "info" : s >= lines.Red ? "alarm" : s >= lines.Amber ? "warning" : "good";

    /// <summary>The Central days the window covers, oldest first, each as its start.</summary>
    /// <param name="from">The window's start.</param>
    /// <param name="to">The window's end.</param>
    public static IReadOnlyList<DateTimeOffset> Days(DateTimeOffset from, DateTimeOffset to)
    {
        var days = new List<DateTimeOffset>();
        for (var day = NightlySlots.StartOfCentralDay(from); day < to; day = NightlySlots.StartOfCentralDay(day.AddHours(25)))
        {
            days.Add(day);
        }
        return days;
    }

    /// <summary>Tokens out a Central day, one series a lane in the order the lanes first appear, each day as a share of the busiest lane-day.</summary>
    /// <param name="records">The window's documents.</param>
    /// <param name="days">The window's Central days.</param>
    public static IReadOnlyList<CloudSeries> LaneSeries(IReadOnlyList<UsageRecord> records, IReadOnlyList<DateTimeOffset> days)
    {
        var lanes = records.OrderBy(record => record.At).Select(record => record.Lane).Distinct(StringComparer.Ordinal).ToList();
        long Sum(string lane, DateTimeOffset day) => records
            .Where(record => record.Lane == lane && !record.Dry && NightlySlots.StartOfCentralDay(record.At) == day)
            .Sum(record => record.TokensOut);
        double busiest = lanes.SelectMany(lane => days.Select(day => (double)Sum(lane, day))).DefaultIfEmpty(0).Max();
        return [.. lanes.Select(lane => new CloudSeries(lane, [.. days.Select(day => new CloudPoint(day.ToUnixTimeMilliseconds(), busiest > 0 ? Math.Round(Sum(lane, day) / busiest * 100, 1) : 0))]))];
    }

    /// <summary>A count of tokens as the card prints it.</summary>
    public static string Tokens(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A cost as the card prints it, always marked as the API-equivalent price.</summary>
    public static string Cost(decimal? dollars) => dollars is { } d ? "$" + d.ToString("0.00", CultureInfo.InvariantCulture) + " API-equivalent" : "no cost reported";

    /// <summary>The runs, tokens out and cost of a set of documents, in a line.</summary>
    public static string Totals(IReadOnlyList<UsageRecord> records)
    {
        decimal? cost = records.Any(record => record.CostUsd is not null) ? records.Sum(record => record.CostUsd ?? 0) : null;
        return $"{records.Count} {(records.Count == 1 ? "run" : "runs")}, {Tokens(records.Sum(record => record.TokensOut))} tokens out, {Cost(cost)}";
    }

    /// <param name="inputs">The card's inputs.</param>
    /// <param name="source">The usage documents as last read, and where they are kept.</param>
    /// <param name="runs">The nightly run log, newest first; empty where there is none.</param>
    /// <param name="repo">The repository the nightly workflow runs in, for the run links; null for none.</param>
    public static CloudReport View(ResourceCardInputs inputs, ClaudeSource source, IReadOnlyList<NightlyRun> runs, string? repo)
    {
        var records = source.Records;
        string said = source.Said;
        var window = InWindow(inputs, records);
        double? share = TodayShare(records, inputs.Now);
        double? average = WeekAverage(records, inputs.Now);
        long today = Today(records, inputs.Now);
        var dials = new[]
        {
            new CloudDial("Today against the 7-day average", share, PlanView.Percent(share),
                average is { } a ? $"{Tokens(today)} tokens out today, a {Tokens((long)a)} average" : "no runs in the seven days before today",
                CloudThresholds.ClaudeDay.Amber, CloudThresholds.ClaudeDay.Red, null, null, Tone(share, CloudThresholds.ClaudeDay)),
            new CloudDial("Plan week used", null, PlanView.Percent(null), PlanUnread, CloudThresholds.ClaudePlanWeek.Amber, CloudThresholds.ClaudePlanWeek.Red, null, null, "info"),
        };
        var week = Days(inputs.Now - TimeSpan.FromDays(6), inputs.Now);
        IReadOnlyList<double?> PerDay(Func<IEnumerable<UsageRecord>, double> measure) =>
            [.. week.Select(day => (double?)measure(records.Where(record => !record.Dry && NightlySlots.StartOfCentralDay(record.At) == day)))];
        var sparks = new[]
        {
            new CloudSpark("Tokens out, 7 days", PerDay(day => day.Sum(record => record.TokensOut))),
            new CloudSpark("Runs, 7 days", PerDay(day => day.Count())),
        };
        var chart = new CloudChart("Tokens a day, by lane", LaneSeries(window, Days(inputs.Now - inputs.Length, inputs.Now)), [], 100,
            "one line a lane, in the order the lanes first appear · each day's tokens out as a share of the busiest lane-day in the window · dry runs left out",
            (inputs.Now - inputs.Length).ToUnixTimeMilliseconds());
        var newest = records.Count > 0 ? records[0] : null;
        var weekRecords = records.Where(record => record.At >= inputs.Now - TimeSpan.FromDays(7)).ToList();
        var todays = records.Where(record => record.At >= NightlySlots.StartOfCentralDay(inputs.Now)).ToList();
        var readout = new CloudReadout(
            "Claude usage",
            share,
            "of the 7-day average, today",
            [
                new("Last run", newest is null ? "none reported" : ClaudeRows.RunWords(newest), "info") { Source = Documents },
                new("Today", Totals(todays), "info") { Source = Documents },
                new("This week", Totals(weekRecords), "info") { Source = Documents },
                new("Kept", said, "info") { Source = CardFrame.ThisProcess },
                ResourceView.LiveRead(inputs),
            ],
            "Every dollar is the API-equivalent price of a run billed to the plan, never a bill; the month's bill is on the nightly card. Dry runs are listed and left out of every average.");
        bool unread = said.StartsWith("not read", StringComparison.Ordinal);
        string? note = unread ? char.ToUpperInvariant(said[0]) + said[1..] + "."
            : records.Count == 0 ? "No usage document has been posted yet; the nightly lane posts one after each run once its key is set."
            : ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute"));
        int lanes = records.Select(record => record.Lane).Distinct(StringComparer.Ordinal).Count();
        return new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && !unread,
            note,
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Claude usage",
            $"{lanes} {(lanes == 1 ? "LANE" : "LANES")} · 30 DAYS READ",
            new CloudLink("the usage documents", null),
            dials,
            sparks,
            chart,
            ClaudeRows.Rows(window, runs.Where(run => run.At >= inputs.Now - inputs.Length).ToList(), repo),
            readout);
    }
}
