// The month at yesterday's rate, the cost card's line under the forecast (ADR: What Azure charges): its own
// file because it is one rule with its own edge cases, the day still being added to and the month's first day.
using System.Globalization;

namespace TheYard.Api.Shared.CostReadings;

/// <summary>The month if every day left costs what the last finished day did.</summary>
public static class YesterdayRate
{
    /// <summary>
    /// The month if every day left costs what the last finished day did: the finished days of the month,
    /// plus that day's cost for each day after it to the month's end (a day Azure is still adding to counts
    /// at whichever is more, its part so far or the full day). So a spike shows as a projection the day it
    /// starts, where a window's average takes a week to move. Null when no finished day is kept.
    /// </summary>
    public static (double Month, string Day, double Cost)? For(IReadOnlyList<CostDay> days, string newest, bool partial, string month, double monthToDate)
    {
        var byDay = days.GroupBy(day => day.Day).ToDictionary(group => group.Key, group => group.Sum(day => day.Cost), StringComparer.Ordinal);
        string? finished = byDay.Keys
            .Where(day => !(partial && day == newest))
            .OrderByDescending(day => day, StringComparer.Ordinal)
            .FirstOrDefault();
        if (finished is null)
        {
            return null;
        }

        double rate = byDay[finished];
        double partCost = partial && CostWindows.MonthOf(newest) == month ? byDay.GetValueOrDefault(newest) : 0;
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = first.AddMonths(1).AddDays(-1);
        var from = DateOnly.ParseExact(finished, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);
        if (from < first)
        {
            from = first;
        }

        int left = Math.Max(0, last.DayNumber - from.DayNumber + 1);
        double finishedSoFar = monthToDate - partCost;
        // The day being added to is one of the days left: it counts at the full day's rate, or at its own
        // part when that is already more.
        double atRate = finishedSoFar + (left * rate) + (partCost > rate ? partCost - rate : 0);
        return (CostView.Money(atRate), finished, CostView.Money(rate));
    }
}
