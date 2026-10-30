namespace JobHunt.Domain.Shared.Clock;

/// <summary>
/// The job hunt's slots in Saint Charles time. Flex Consumption on Linux honours neither
/// WEBSITE_TIME_ZONE nor TZ (learn.microsoft.com/azure/azure-functions/functions-bindings-timer),
/// so a timer expression can only be written in UTC; it therefore fires every hour on the hour and
/// this asks what, if anything, that hour is in America/Chicago, which is the only way a schedule
/// stated in local hours survives a daylight-saving change. The reply lane runs at 4:00 AM, and
/// the four-hour records run at midnight, 8 AM, noon, 4 PM and 8 PM. The table itself
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
    /// The campaign's hour, which it sends in automatically, seven days a week (ADR-118): the 8 o'clock hour
    /// in Central, under daylight time or standard time alike. It is asked as a local hour rather than written
    /// into the timer as a UTC one, so the hourly UTC timer finds it on both sides of the November clock change
    /// instead of the send sliding an hour twice a year.
    /// </summary>
    public static bool IsCampaignHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 8;

    /// <summary>The run this hour starts, or null when the hour is not a slot.</summary>
    public static string? Slot(DateTimeOffset utcNow) => NightlySlots.Slot(utcNow);
}
