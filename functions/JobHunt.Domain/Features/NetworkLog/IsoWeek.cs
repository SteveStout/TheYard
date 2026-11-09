using System.Globalization;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// The scoreboard's week: the ISO week (Monday to Sunday, week 1 holding the year's first Thursday) of a Central
/// day, and how many conversations began in it.
/// </summary>
public static class IsoWeek
{
    /// <summary>The ISO year and week of a Central day.</summary>
    public static (int Year, int Week) Of(DateOnly centralDay)
    {
        var day = centralDay.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day));
    }

    /// <summary>The Monday the ISO week of a Central day starts on.</summary>
    public static DateOnly Monday(DateOnly centralDay) => centralDay.AddDays(-(((int)centralDay.DayOfWeek + 6) % 7));

    /// <summary>The rows whose first two-way exchange fell, in Central time, in the ISO week of the day.</summary>
    public static int StartedThisWeek(IEnumerable<Conversation> rows, DateOnly centralDay)
    {
        var week = Of(centralDay);
        return rows.Count(row => Of(DateOnly.FromDateTime(CentralClock.Local(row.FirstTwoWayAt))) == week);
    }
}
