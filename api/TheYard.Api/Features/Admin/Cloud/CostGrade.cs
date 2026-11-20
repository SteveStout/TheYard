// How the two cost dials are graded (ADR-102, the addendum on what a colour means): the Function's free grant and
// Insights' cost row each read their own month's cost past the free amount, at the last day's rate, and the last day
// against the seven before it. Green is a month working as expected, amber a cost or a spike worth researching, and red
// only a card past its own line while the subscription's month forecast is past the budget. Every line is in
// CloudThresholds; CostGradeTests holds each one from both sides.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

// #region cost-grade
/// <summary>The tone, the words and the chips of a card graded on what its month costs past its free amount.</summary>
public static class CostGrade
{
    /// <summary>How many days before the last one its median is taken over: a week, as the bill's own spike rule.</summary>
    public const int Week = 7;

    /// <summary>
    /// The tone: red ("alarm") when the card's projected cost past its free amount is at or past its line and the
    /// subscription's month forecast is past the budget; amber ("warning") when the cost is at or past its line, or the
    /// last day is at or past its spike line over the week's median; green ("good") otherwise; information when the
    /// cost is not read, because a card with no reading never reads green.
    /// </summary>
    /// <param name="pastFreeDollars">What the card's month comes to past its free amount, projected, in dollars; null when not read.</param>
    /// <param name="lastDay">The last twenty-four hours' use, in the card's own unit; null when not read.</param>
    /// <param name="weekMedian">The median of the seven days before it, in the same unit; null when none is kept.</param>
    /// <param name="forecast">The subscription's month forecast from the bill, in dollars; null when not read.</param>
    public static string Tone(double? pastFreeDollars, double? lastDay, double? weekMedian, double? forecast)
    {
        if (pastFreeDollars is not { } dollars)
        {
            return "info";
        }
        bool over = dollars >= CloudThresholds.CostPastFreeDollars;
        return over && forecast > CloudThresholds.CostBudgetDollars ? "alarm"
            : over || Spiked(lastDay, weekMedian) ? "warning"
            : "good";
    }

    /// <summary>True when the last day is at or past the spike line over the week's median; never with no median to read against.</summary>
    /// <param name="lastDay">The last twenty-four hours' use.</param>
    /// <param name="weekMedian">The median of the seven days before it.</param>
    public static bool Spiked(double? lastDay, double? weekMedian) =>
        lastDay is { } day && weekMedian is > 0 && day >= CloudThresholds.CostDaySpike * weekMedian.Value;

    /// <summary>The projected use past the free amount, in dollars at its price; zero inside the free amount, null when not read.</summary>
    /// <param name="projected">The month's use projected to its last day, or used so far before it is projected.</param>
    /// <param name="free">The free amount, in the same unit.</param>
    /// <param name="dollarsEach">The price of one unit past it.</param>
    public static double? PastFree(double? projected, double free, double dollarsEach) =>
        projected is { } use ? Math.Round(Math.Max(0, use - free) * dollarsEach, 2) : null;

    /// <summary>The middle of the days given, or the mean of the middle two; null when none is given.</summary>
    /// <param name="days">The days' uses.</param>
    public static double? Median(IReadOnlyList<double> days)
    {
        if (days.Count == 0)
        {
            return null;
        }
        var sorted = days.Order().ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// The seven twenty-four-hour totals before the last one, newest first, each the sum of a reading over its day of the
    /// kept five-minute buckets; a day with no bucket kept is left out, so a card read for three days has three.
    /// </summary>
    /// <param name="month">The thirty days at five minutes.</param>
    /// <param name="reading">The summed reading.</param>
    /// <param name="now">The card's instant.</param>
    public static IReadOnlyList<double> DaysBefore(IReadOnlyList<ResourceBucket> month, string reading, DateTimeOffset now) =>
        [.. Enumerable.Range(1, Week)
            .Select(back => month.Where(b => b.At >= now.AddDays(-back - 1) && b.At < now.AddDays(-back)).Select(b => b.Sums.GetValueOrDefault(reading)).Where(v => v is not null).ToList())
            .Where(day => day.Count > 0)
            .Select(day => day.Sum(v => v!.Value))];

    /// <summary>
    /// The seven last-day readings before the newest, newest first: a reading that is itself a rolling day's total (the
    /// workspace's "last day"), taken at the newest bucket kept in the two hours up to one day back, two days back and so
    /// on; a day with nothing kept there is left out.
    /// </summary>
    /// <param name="month">The thirty days at five minutes.</param>
    /// <param name="reading">The rolling day's reading.</param>
    /// <param name="now">The card's instant.</param>
    public static IReadOnlyList<double> RollingDaysBefore(IReadOnlyList<ResourceBucket> month, string reading, DateTimeOffset now) =>
        [.. Enumerable.Range(1, Week)
            .Select(back => month.Where(b => b.At <= now.AddDays(-back) && b.At > now.AddDays(-back).AddHours(-2) && b.Means.GetValueOrDefault(reading) is not null).Select(b => b.Means[reading]).LastOrDefault())
            .Where(v => v is not null)
            .Select(v => v!.Value)];

    /// <summary>The grade in words for the card's cost row: the projected dollars, the day against its week and the forecast against the budget.</summary>
    /// <param name="pastFreeDollars">The projected cost past the free amount.</param>
    /// <param name="free">The free amount in words: "the free 5 GB".</param>
    /// <param name="lastDay">The last day's use.</param>
    /// <param name="weekMedian">The week's median.</param>
    /// <param name="forecast">The subscription's month forecast.</param>
    public static string Words(double? pastFreeDollars, string free, double? lastDay, double? weekMedian, double? forecast) =>
        string.Join(" · ", new[]
        {
            pastFreeDollars is { } dollars ? $"about {Dollars(dollars)} past {free} by the month's end" : $"no cost past {free} until the month's use is kept",
            lastDay is { } day && weekMedian is > 0 ? $"the last day {(day / weekMedian.Value).ToString("0.0", CultureInfo.InvariantCulture)} times the median of the {Week} days before it" : "no week kept to read the last day against",
            forecast is { } f ? $"the subscription's month forecast {Dollars(f)} against the {Dollars(CloudThresholds.CostBudgetDollars)} budget" : "the subscription's month forecast is not on the bill yet",
            Rule,
        });

    /// <summary>The rule in one sentence, as the cost row prints it under its figures.</summary>
    public static readonly string Rule =
        $"green under {Dollars(CloudThresholds.CostPastFreeDollars)} past the free amount and a day under {CloudThresholds.CostDaySpike.ToString("0.0", CultureInfo.InvariantCulture)} times its week, red only past {Dollars(CloudThresholds.CostPastFreeDollars)} with the forecast past the {Dollars(CloudThresholds.CostBudgetDollars)} budget";

    /// <summary>The three lines as chips for the card's thresholds, after its dials' own.</summary>
    public static IReadOnlyList<CloudChip> Chips { get; } =
    [
        new($"cost amber {Dollars(CloudThresholds.CostPastFreeDollars)} past the free amount", "amber"),
        new($"cost amber at {CloudThresholds.CostDaySpike.ToString("0.0", CultureInfo.InvariantCulture)} times the {Week}-day median", "amber"),
        new($"cost red past {Dollars(CloudThresholds.CostPastFreeDollars)} with the month's forecast past {Dollars(CloudThresholds.CostBudgetDollars)}", "red"),
    ];

    /// <summary>Dollars as the cards print them: "$5.87".</summary>
    private static string Dollars(double dollars) => "$" + dollars.ToString("0.00", CultureInfo.InvariantCulture);
}
// #endregion cost-grade
