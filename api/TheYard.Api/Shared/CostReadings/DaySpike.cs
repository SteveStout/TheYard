// The days the spike rule reads (ADR: What Azure charges): each day with the median of the finished week
// before it and the resource that cost the most that day. Facts only: the line a day must pass and the
// tone it reads live in the page's one table of ranges, so the rule is never written twice.
namespace TheYard.Api.Shared.CostReadings;

/// <summary>One day as the spike rule reads it.</summary>
/// <param name="Day">The UTC day, as yyyy-MM-dd.</param>
/// <param name="Cost">What the day cost.</param>
/// <param name="Partial">True for the newest day while Azure is still adding to it.</param>
/// <param name="Median">The median of up to seven finished days before it, or null when none is kept.</param>
/// <param name="Days">How many finished days that median is taken over.</param>
/// <param name="Multiple">The day's cost over that median, to one place, or null with no median.</param>
/// <param name="Biggest">The resource with the biggest line that day.</param>
/// <param name="BiggestCost">What that resource cost that day.</param>
public sealed record SpikeDay(string Day, double Cost, bool Partial, double? Median, int Days, double? Multiple, string? Biggest, double BiggestCost);

/// <summary>The judged day and the seven before it, newest first, each beside the week before it.</summary>
public static class DaySpike
{
    /// <summary>How many days the card draws: the judged day and the seven before it.</summary>
    public const int Shown = 8;

    /// <summary>How many finished days a day's median is taken over.</summary>
    public const int Week = 7;

    /// <summary>
    /// The newest <see cref="Shown"/> days up to <paramref name="newest"/>, newest first. Each carries the
    /// median of the finished days before it (up to seven; fewer when fewer are kept, and the count says so)
    /// and the resource that cost the most that day. Empty when no day is kept, never null.
    /// </summary>
    public static IReadOnlyList<SpikeDay> For(IReadOnlyList<CostDay> days, string newest, bool partial)
    {
        var byDay = days
            .Where(day => string.CompareOrdinal(day.Day, newest) <= 0)
            .GroupBy(day => day.Day)
            .OrderByDescending(group => group.Key, StringComparer.Ordinal)
            .Select(group => (Day: group.Key, Cost: group.Sum(day => day.Cost), Lines: group))
            .ToList();
        var shown = new List<SpikeDay>();
        for (int at = 0; at < byDay.Count && at < Shown; at++)
        {
            var (day, cost, lines) = byDay[at];
            var week = byDay.Skip(at + 1).Take(Week).Select(entry => entry.Cost).ToList();
            double? median = week.Count == 0 ? null : CostView.Money(Median(week));
            var biggest = lines
                .GroupBy(line => line.Resource)
                .Select(group => (Name: group.Key, Cost: group.Sum(line => line.Cost)))
                .OrderByDescending(entry => entry.Cost)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .First();
            double? multiple = median is > 0 ? Math.Round(cost / median.Value, 1) : null;
            shown.Add(new SpikeDay(day, CostView.Money(cost), partial && day == newest, median, week.Count, multiple, biggest.Name, CostView.Money(biggest.Cost)));
        }

        return shown;
    }

    /// <summary>The middle value, or the mean of the middle two.</summary>
    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
