namespace JobHunt.Functions;

/// <summary>
/// The job hunt's slots in Saint Charles time. Flex Consumption on Linux does not honour
/// WEBSITE_TIME_ZONE or TZ (learn.microsoft.com/azure/azure-functions/functions-bindings-timer,
/// read 2026-10-20), so the timer fires every hour on the hour in UTC and this decides what,
/// if anything, that hour is in America/Chicago: 4:00 AM is the reply lane, and midnight, 8 AM,
/// noon, 4 PM and 8 PM are the four-hour records run (Steve, 2026-10-20 15:26).
/// </summary>
public static class CentralClock
{
    public const string Reply = "nightly";
    public const string Bookkeeping = "bookkeeping";

    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static DateTime Local(DateTimeOffset utcNow) => TimeZoneInfo.ConvertTime(utcNow, Central).DateTime;

    public static bool IsFourAm(DateTimeOffset utcNow) => Local(utcNow).Hour == 4;

    /// <summary>The run this hour starts, or null when the hour is not a slot.</summary>
    public static string? Slot(DateTimeOffset utcNow) => Local(utcNow).Hour switch
    {
        4 => Reply,
        0 or 8 or 12 or 16 or 20 => Bookkeeping,
        _ => null,
    };
}
