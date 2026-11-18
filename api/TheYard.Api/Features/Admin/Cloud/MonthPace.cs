// The month-end pace check every monthly allowance on the Cloud cards shares (ADR-102, the addendum on pace and stale
// cards): the month's use so far, carried at its own pace to the month's last day and read against the allowance, so
// a free grant that will run out turns amber while there is still time to act, not on the day it does.

using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>Where a month's use is heading at its pace so far, and the tone that gives against an allowance.</summary>
public static class MonthPace
{
    /// <summary>The days the month must have run before its pace is judged: before that, one busy night is most of the month.</summary>
    public const double DaysBeforeJudged = 3;

    /// <summary>The use the month reaches by its last day at its pace so far; null before it is judged or when nothing is read.</summary>
    /// <param name="used">The month's use so far, in the allowance's own unit.</param>
    /// <param name="now">The reading's instant.</param>
    public static double? Projected(double? used, DateTimeOffset now)
    {
        if (used is not { } so)
        {
            return null;
        }
        var utc = now.ToUniversalTime();
        double elapsed = (utc - new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero)).TotalDays;
        return elapsed < DaysBeforeJudged ? null : so * DateTime.DaysInMonth(utc.Year, utc.Month) / elapsed;
    }

    /// <summary>The tone given, lifted to amber ("warning") when the month's pace passes its allowance; a warning or alarm already earned stays.</summary>
    /// <param name="tone">The reading's own tone, as the wire writes it.</param>
    /// <param name="projected">The month's use at its pace, from <see cref="Projected"/>.</param>
    /// <param name="allowance">The allowance, in the same unit.</param>
    public static string Tone(string tone, double? projected, double allowance) =>
        tone is "warning" or "alarm" ? tone : projected is { } p && p >= allowance ? "warning" : tone;

    /// <summary>The pace in words for a line: "at this pace 7.55 by the month's end", or "too early in the month to project".</summary>
    /// <param name="projected">The month's use at its pace.</param>
    /// <param name="format">How the figure is written, with its unit: "{0:0.00} GB".</param>
    public static string Words(double? projected, string format) =>
        projected is { } p ? "at this pace " + string.Format(CultureInfo.InvariantCulture, format, p) + " by the month's end" : "too early in the month to project";
}
