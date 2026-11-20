// The month-end pace check every monthly allowance on the Cloud cards shares (ADR-102, the addendum on pace and stale
// cards): the month's use so far, carried at its own pace to the month's last day. CostGrade turns where it is heading
// into dollars past the allowance, which is what the two cost dials are graded on (the addendum on what a colour means).

using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>Where a month's use is heading at its pace so far.</summary>
public static class MonthPace
{
    /// <summary>The days the month must have run before its pace is judged: before that, one busy night is most of the month.</summary>
    public const double DaysBeforeJudged = 3;

    /// <summary>
    /// The use the month reaches by its last day: what is used so far plus the last day's rate for every day left, when the
    /// last day is read; the month's own pace carried to its last day when it is not. A straight line from the first
    /// cannot see a cut or a spike made mid-month, so the last day's rate is the truer projection in both directions
    /// (ADR-102, the addendum on the honesty rule). Null before the month is judged or when nothing is read.
    /// </summary>
    /// <param name="used">The month's use so far, in the allowance's own unit.</param>
    /// <param name="now">The reading's instant.</param>
    /// <param name="lastDay">The use in the last twenty-four hours, in the same unit; null when not read.</param>
    public static double? Projected(double? used, DateTimeOffset now, double? lastDay = null)
    {
        if (used is not { } so)
        {
            return null;
        }
        var utc = now.ToUniversalTime();
        double elapsed = (utc - new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero)).TotalDays;
        int days = DateTime.DaysInMonth(utc.Year, utc.Month);
        return elapsed < DaysBeforeJudged ? null : lastDay is { } rate ? so + rate * Math.Max(0, days - elapsed) : so * days / elapsed;
    }

    /// <summary>What a whole month comes to at the last day's rate; null when the last day is not read.</summary>
    /// <param name="lastDay">The use in the last twenty-four hours.</param>
    /// <param name="now">The reading's instant, whose month gives the days.</param>
    public static double? RunRate(double? lastDay, DateTimeOffset now) =>
        lastDay is { } rate ? rate * DateTime.DaysInMonth(now.ToUniversalTime().Year, now.ToUniversalTime().Month) : null;

    /// <summary>The pace in words for a line: "at this pace 7.55 by the month's end", or "too early in the month to project".</summary>
    /// <param name="projected">The month's use at its pace.</param>
    /// <param name="format">How the figure is written, with its unit: "{0:0.00} GB".</param>
    /// <param name="rated">True when the projection carries the last day's rate, which the words then name.</param>
    public static string Words(double? projected, string format, bool rated = false) =>
        projected is { } p ? (rated ? "at the last day's rate " : "at this pace ") + string.Format(CultureInfo.InvariantCulture, format, p) + " by the month's end" : "too early in the month to project";
}
