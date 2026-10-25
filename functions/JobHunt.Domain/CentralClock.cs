namespace JobHunt.Domain;

/// <summary>
/// The job hunt's slots in Saint Charles time. Flex Consumption on Linux does not honour
/// WEBSITE_TIME_ZONE or TZ (learn.microsoft.com/azure/azure-functions/functions-bindings-timer,
/// read 2026-10-20), so the timer fires every hour on the hour in UTC and this asks what, if
/// anything, that hour is in America/Chicago: 4:00 AM is the reply lane, and midnight, 8 AM,
/// noon, 4 PM and 8 PM are the four-hour records run (Steve, 2026-10-20 15:26). The table itself
/// is TheYard.Domain's NightlySlots, compiled into this project by a link in the .csproj, so the
/// Admin tab's card and this clock read one table and cannot drift apart.
/// </summary>
public static class CentralClock
{
    public const string Reply = NightlySlots.Reply;
    public const string Bookkeeping = NightlySlots.Bookkeeping;

    public static DateTime Local(DateTimeOffset utcNow) => NightlySlots.Local(utcNow);

    public static bool IsFourAm(DateTimeOffset utcNow) => NightlySlots.Slot(utcNow) == NightlySlots.Reply;

    /// <summary>
    /// The campaign's hour (ADR-118; Steve, 10/24: "It sends automatically at 8:00 am", seven days a week): the
    /// 8 o'clock hour in Central, under daylight time or standard time alike, so the hourly UTC timer finds it on
    /// both sides of the November clock change.
    /// </summary>
    public static bool IsCampaignHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 8;

    /// <summary>The run this hour starts, or null when the hour is not a slot.</summary>
    public static string? Slot(DateTimeOffset utcNow) => NightlySlots.Slot(utcNow);
}
