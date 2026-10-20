namespace JobHunt.Functions;

/// <summary>
/// 4:00 AM in Saint Charles, all year. Flex Consumption on Linux does not honour
/// WEBSITE_TIME_ZONE or TZ (learn.microsoft.com/azure/azure-functions/functions-bindings-timer,
/// read 2026-10-20), so the timer fires at 09:00 and 10:00 UTC and only the firing that
/// lands on 4 o'clock Central does the work: 09:00 UTC under CDT, 10:00 UTC under CST.
/// </summary>
public static class CentralClock
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static DateTime Local(DateTimeOffset utcNow) => TimeZoneInfo.ConvertTime(utcNow, Central).DateTime;

    public static bool IsFourAm(DateTimeOffset utcNow) => Local(utcNow).Hour == 4;
}
