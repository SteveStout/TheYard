// The job hunt Function's processes as the nightly card names them (ADR-109, the addendum on the six
// processes): each one's name in the Function, the title the card gives it, its switches, its timer's
// schedule and that schedule in words, and when it next runs. One table, held by a test, so the card's
// words and the Function's cron strings cannot drift apart unnoticed. Pure: the moment goes in, the
// next run comes out; nothing here reads the clock.
using System.Globalization;

namespace TheYard.Domain.Features.Admin;

// #region function-processes
/// <summary>One of the Function's processes on the card.</summary>
/// <param name="Key">Its name in the Function and in Application Insights, the run line's process: every RunFloors row has one (the watch's W12 reads each).</param>
/// <param name="Title">What the card calls it: "The inbox pass".</param>
/// <param name="Cron">The timer's schedule as the Function writes it (seconds first); null for the health route, which runs on request, and for a process that rides another's timer.</param>
/// <param name="Schedule">The schedule in words.</param>
/// <param name="Switches">The app settings that turn it on, shadow or off, the one that decides its tone first; none for the health route.</param>
/// <param name="ShowsQuietRuns">Whether its run history lists every run; false lists only the runs that did something, and the rest are counted.</param>
public sealed record FunctionProcess(string Key, string Title, string? Cron, string Schedule, IReadOnlyList<string> Switches, bool ShowsQuietRuns);

/// <summary>The processes in the card's order, and when each next runs.</summary>
public static class FunctionProcesses
{
    /// <summary>The processes, in the order the card draws them.</summary>
    public static readonly IReadOnlyList<FunctionProcess> All =
    [
        new("AtsSweep", "The inbox pass", "0 20,50 * * * *", "Every 30 minutes, at 20 and 50 past (UTC and Central alike).", ["ATS_SWEEP", "LEADS", "LEADS_LIST", "TRIAGE", "REPLY_ON_HUMAN"], true),
        new("Merger", "The tracker merger", "0 */2 * * * *", "Every two minutes, on the even minute.", ["MERGER", "APPENDS", "TRACKER_SQL"], false),
        new("Nightly", "The clock", "0 0 * * * *", "Every hour on the hour; acts at 4 AM, midnight, 8 AM, noon, 4 PM and 8 PM Central.", ["NIGHTLY_ANY_HOUR"], false),
        new("SheetMirror", "The Sheet mirror", "0 7,22,37,52 * * * *", "Every 15 minutes, at 7, 22, 37 and 52 past, clear of the merger's minute and the clock's hour.", ["SHEET_MIRROR"], true),
        new("DailyCard", "The daily card", "0 5 * * * *", "Every hour at 5 past; always writes at 6 AM Central.", ["DAILY_CARD"], false),
        new("TodayCard", "The morning card", "0 30 * * * *", "Every hour at 30 past; writes and mails TODAY.md once a day, at 7:30 Central. TODAY_CARD unset reads as on.", ["TODAY_CARD"], false),
        new("Funnel", "The funnel", null, "No timer of its own: the morning card's 7:30 tick writes the day before's line once a day; a hand run writes any day's. FUNNEL unset reads as on.", ["FUNNEL"], true),
        new("CampaignSend", "The email campaign", "0 0 * * * *", "Every hour on the hour; acts at 8 AM Central, seven days a week.", ["EMAIL_CAMPAIGN"], false),
        new("NetworkLogPass", "The network log", "0 0 * * * *", "Every hour on the hour; acts once a day in the 6 AM hour Central.", ["NETWORK_LOG", "NETWORK_LOG_ANY_HOUR", "NETWORK_SEND"], false),
        new("MergerHealth", "The merger's health", null, "On request; this card asks once a minute.", [], true),
        new("BoardScan", "The board scan", "0 0 * * * *", "Every hour on the hour; acts once a day in the 1 AM hour Central.", ["CAMPAIGN_SQL"], false),
        new("FreshScan", "The fresh pass", "0 */15 * * * *", "Every 15 minutes, on the quarter hour.", ["FRESH", "CAMPAIGN_SQL"], false),
        new("Pounce", "The pounce", "0 5/15 * * * *", "Every 15 minutes, at 5, 20, 35 and 50 past, five minutes behind the fresh pass.", ["POUNCE", "POUNCE_RUN"], false),
        new("PounceDay", "The pounce's day line", null, "No timer of its own: the pounce's first tick after midnight Central writes the day before's line.", ["POUNCE"], true),
        new("PounceFollowUp", "The pounce follow-up", "0 10/15 * * * *", "Every 15 minutes, at 10, 25, 40 and 55 past, five minutes behind the pounce.", ["POUNCE_RUN"], false),
        new("ReplyDraft", "The reply drafts", "0 2-59/5 * * * *", "Every 5 minutes from 2 past, off the sweep's 20 and 50.", ["POUNCE_RUN"], false),
        new("Watch", "The watch", "0 13,28,43,58 * * * *", "Every 15 minutes, at 13, 28, 43 and 58 past, clear of every other timer's minute. WATCH shadow writes WATCH.md and heals nothing.", ["WATCH"], true),
        new("TodayCardHandRun", "The morning card by hand", null, "On request, with the function key.", ["TODAY_CARD"], true),
        new("CampaignTestSend", "The campaign's test send", null, "On request, with the function key; one test email to the test address.", ["CAMPAIGN_SQL"], true),
    ];

    /// <summary>
    /// The processes that tick every hour and act in one hour Central only, at one minute of it: the campaign at 8
    /// (ADR-118), the network log at 6, and the morning card at 7:30.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Hour, int Minute)> ActsOnce = new Dictionary<string, (int Hour, int Minute)>(StringComparer.Ordinal)
    {
        ["CampaignSend"] = (8, 0),
        ["NetworkLogPass"] = (6, 0),
        ["TodayCard"] = (7, 30),
        ["BoardScan"] = (1, 0),
    };

    /// <summary>
    /// The processes with no timer or request of their own, each by the process whose tick runs it: the funnel's line
    /// is written inside the morning card's tick, so it has no request row named for it and runs when the card does.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Riders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Funnel"] = "TodayCard",
        ["PounceDay"] = "Pounce",
    };

    /// <summary>The process by its key, or null for a name that is not one of the table's.</summary>
    /// <param name="key">The name from the route, matched exactly.</param>
    public static FunctionProcess? Find(string? key) => All.FirstOrDefault(process => string.Equals(process.Key, key, StringComparison.Ordinal));

    /// <summary>Every key in the card's order as one phrase: "AtsSweep, Merger, ... and MergerHealth".</summary>
    public static string Named() => string.Join(", ", All.SkipLast(1).Select(process => process.Key)) + " and " + All[^1].Key;

    /// <summary>Whether a process runs only when it is asked by hand: no timer, no host tick to ride, and not the health route the card asks.</summary>
    /// <param name="process">One of the table's.</param>
    public static bool ByHand(FunctionProcess process) => process.Cron is null && !Riders.ContainsKey(process.Key) && process.Key != "MergerHealth";

    /// <summary>The Central hour a process that ticks hourly acts in, or null for one that acts whenever it runs.</summary>
    /// <param name="key">The process's key.</param>
    public static int? ActingHour(string key) => ActsOnce.TryGetValue(key, out var acts) ? acts.Hour : null;

    /// <summary>
    /// When the process next runs after <paramref name="after"/>: the timer's next minute; for the clock, the
    /// next hour that is a slot (the hours between it runs and does nothing); for a process that acts in one hour
    /// Central, that hour's minute next; for a process that rides another's tick, that one's next; for the health
    /// route, the card's next minute.
    /// </summary>
    /// <param name="process">One of the table's.</param>
    /// <param name="after">The moment to look from, UTC.</param>
    public static DateTimeOffset Next(FunctionProcess process, DateTimeOffset after)
    {
        if (Riders.TryGetValue(process.Key, out string? host))
        {
            return Next(Find(host)!, after);
        }
        if (process.Key == "Nightly")
        {
            return NightlySlots.Next(after).At;
        }
        if (ActsOnce.TryGetValue(process.Key, out var acts))
        {
            // Hourly in UTC like the clock, and acts in one hour Central only, at its own minute of the hour.
            for (var tick = Minute(after).AddMinutes(acts.Minute - Minute(after).Minute); ; tick = tick.AddHours(1))
            {
                if (tick > Minute(after) && NightlySlots.Local(tick).Hour == acts.Hour)
                {
                    return tick;
                }
            }
        }
        if (process.Cron is not { } cron)
        {
            return Minute(after).AddMinutes(1);
        }
        string[] fields = cron.Split(' ');
        var minutes = Field(fields[1], 60);
        var hours = Field(fields[2], 24);
        for (var at = Minute(after).AddMinutes(1); ; at = at.AddMinutes(1))
        {
            if (minutes.Contains(at.Minute) && hours.Contains(at.Hour))
            {
                return at;
            }
        }
    }

    /// <summary>The values a cron field allows: "*" is every value, else a comma list of numbers.</summary>
    /// <summary>One cron field's values: "*", a list ("7,22,37,52"), a step from a start or a range ("5/15", "*/15", "2-59/5").</summary>
    private static HashSet<int> Field(string field, int count) =>
        [.. field.Split(',').SelectMany(part => Values(part, count))];

    private static List<int> Values(string part, int count)
    {
        string[] stepped = part.Split('/');
        int step = stepped.Length == 2 ? int.Parse(stepped[1], CultureInfo.InvariantCulture) : 1;
        string[] span = stepped[0].Split('-');
        int from = span[0] == "*" ? 0 : int.Parse(span[0], CultureInfo.InvariantCulture);
        int to = span.Length == 2 ? int.Parse(span[1], CultureInfo.InvariantCulture) : stepped.Length == 2 || span[0] == "*" ? count - 1 : from;
        var values = new List<int>();
        for (int value = from; value <= to; value += step)
        {
            values.Add(value);
        }
        return values;
    }

    private static DateTimeOffset Minute(DateTimeOffset at) => new(at.UtcDateTime.Year, at.UtcDateTime.Month, at.UtcDateTime.Day, at.UtcDateTime.Hour, at.UtcDateTime.Minute, 0, TimeSpan.Zero);
}
// #endregion function-processes
