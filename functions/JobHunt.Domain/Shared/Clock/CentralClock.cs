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

    /// <summary>
    /// The board scan's hour: the 1 o'clock hour in Central, asked as a local hour for the campaign's reason, so the
    /// night's postings are read before the 2 o'clock picker ranks them and long before the 8 AM send.
    /// </summary>
    public static bool IsScanHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 1;

    /// <summary>
    /// The night's picker's hour: the 2 o'clock hour in Central, after the 1 AM scan and before the 4 AM reply lane, so
    /// the jobs file is on the share well before 5 AM. Not a slot in NightlySlots: the Admin card judges a month of
    /// firings against that table, and the picker is the clock's own work in an hour the table does not name.
    /// </summary>
    public static bool IsJobsHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 2;

    /// <summary>
    /// The network log's hour: the 6 o'clock hour in Central, asked as a local hour for the same reason as the
    /// campaign's, so the daily pass lands before the morning and before the 8 AM send on both sides of the clock change.
    /// </summary>
    public static bool IsNetworkHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 6;

    /// <summary>
    /// The reply lane's second chance: the 5 o'clock hour in Central. When the 4 AM run left no linkedin-queue.md for
    /// the day (the runner's own setup can fail before the lane begins, and then nothing runs), this hour
    /// dispatches it once more, so the 6 AM messages lane still has a queue to read. Not a slot in NightlySlots, for
    /// the picker's reason: the clock's own work in an hour the table does not name.
    /// </summary>
    public static bool IsReplyRetryHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 5;

    /// <summary>
    /// The morning card's hour: the 7 o'clock hour in Central, after the 6 AM run has applied and written its essays
    /// and before the 8 AM campaign, asked as a local hour so the card lands at 7:30 on both sides of the clock change.
    /// </summary>
    public static bool IsTodayHour(DateTimeOffset utcNow) => Local(utcNow).Hour == 7;

    /// <summary>The run this hour starts, or null when the hour is not a slot.</summary>
    public static string? Slot(DateTimeOffset utcNow) => NightlySlots.Slot(utcNow);
}
