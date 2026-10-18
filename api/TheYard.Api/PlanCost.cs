// The plan's own line of the bill, read from the cost card's kept days rather than typed in, so
// the plan card and the cost card cannot disagree about what the plan costs.
using TheYard.Application;

namespace TheYard.Api;

/// <summary>What the plan has cost this month, from the kept days of the bill.</summary>
public static class PlanCost
{
    /// <summary>The month to date for the plan's resource, in the bill's currency; null when the bill has not been read or does not name it.</summary>
    public static Task<double?> MonthToDateAsync(ICostHistory history, PlanReader reader, TimeProvider clock, CancellationToken cancellation) =>
        reader.PlanName is { } plan ? ForResourceAsync(history, plan, clock, cancellation) : Task.FromResult<double?>(null);

    /// <summary>
    /// The month to date for any resource the bill names, matched on its name without case; every
    /// card under Cloud infrastructure reads its cost line through this.
    /// </summary>
    public static async Task<double?> ForResourceAsync(ICostHistory history, string plan, TimeProvider clock, CancellationToken cancellation)
    {
        if (!(await history.AvailabilityAsync(cancellation)).Available)
        {
            return null;
        }
        var now = clock.GetUtcNow();
        string month = now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var kept = await history.ReadAsync(month + "-01", cancellation);
        var mine = kept.Days.Where(day => day.Day.StartsWith(month, StringComparison.Ordinal) && string.Equals(day.Resource, plan, StringComparison.OrdinalIgnoreCase)).ToList();
        return mine.Count == 0 ? null : Math.Round(mine.Sum(day => day.Cost), 2);
    }
}
