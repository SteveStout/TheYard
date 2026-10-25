// The job hunt Function's seven processes as the nightly card names them (ADR-109, the addendum on the six
// processes): each one's name in the Function, the title the card gives it, its switches, its timer's
// schedule and that schedule in words, and when it next runs. One table, held by a test, so the card's
// words and the Function's cron strings cannot drift apart unnoticed. Pure: the moment goes in, the
// next run comes out; nothing here reads the clock.
namespace TheYard.Domain.Features.Admin;

// #region function-processes
/// <summary>One of the Function's seven processes.</summary>
/// <param name="Key">Its name in the Function and in Application Insights: AtsSweep, Merger, Nightly, SheetMirror, DailyCard, CampaignSend or MergerHealth.</param>
/// <param name="Title">What the card calls it: "The inbox pass".</param>
/// <param name="Cron">The timer's schedule as the Function writes it (seconds first); null for the health route, which runs on request.</param>
/// <param name="Schedule">The schedule in words.</param>
/// <param name="Switches">The app settings that turn it on, shadow or off, the one that decides its tone first; none for the health route.</param>
/// <param name="ShowsQuietRuns">Whether its run history lists every run; false lists only the runs that did something, and the rest are counted.</param>
public sealed record FunctionProcess(string Key, string Title, string? Cron, string Schedule, IReadOnlyList<string> Switches, bool ShowsQuietRuns);

/// <summary>The seven processes in the card's order, and when each next runs.</summary>
public static class FunctionProcesses
{
    /// <summary>The seven, in the order the card draws them.</summary>
    public static readonly IReadOnlyList<FunctionProcess> All =
    [
        new("AtsSweep", "The inbox pass", "0 20,50 * * * *", "Every 30 minutes, at 20 and 50 past (UTC and Central alike).", ["ATS_SWEEP", "LEADS", "LEADS_LIST", "TRIAGE", "REPLY_ON_HUMAN"], true),
        new("Merger", "The tracker merger", "0 * * * * *", "Every minute, on the minute.", ["MERGER", "APPENDS"], false),
        new("Nightly", "The clock", "0 0 * * * *", "Every hour on the hour; acts at 4 AM, midnight, 8 AM, noon, 4 PM and 8 PM Central.", ["NIGHTLY_ANY_HOUR"], false),
        new("SheetMirror", "The Sheet mirror", "0 7,22,37,52 * * * *", "Every 15 minutes, at 7, 22, 37 and 52 past, clear of the merger's minute and the clock's hour.", ["SHEET_MIRROR"], true),
        new("DailyCard", "The daily card", "0 5 * * * *", "Every hour at 5 past; always writes at 6 AM Central.", ["DAILY_CARD"], false),
        new("CampaignSend", "The email campaign", "0 0 * * * *", "Every hour on the hour; acts at 8 AM Central, seven days a week.", ["EMAIL_CAMPAIGN"], false),
        new("MergerHealth", "The merger's health", null, "On request; this card asks once a minute.", [], true),
    ];

    /// <summary>The process by its key, or null for a name that is not one of the seven.</summary>
    /// <param name="key">The name from the route, matched exactly.</param>
    public static FunctionProcess? Find(string? key) => All.FirstOrDefault(process => string.Equals(process.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// When the process next runs after <paramref name="after"/>: the timer's next minute; for the clock, the
    /// next hour that is a slot (the hours between it runs and does nothing); for the campaign, the next 8 o'clock
    /// hour Central; for the health route, the
    /// card's next minute.
    /// </summary>
    /// <param name="process">One of the seven.</param>
    /// <param name="after">The moment to look from, UTC.</param>
    public static DateTimeOffset Next(FunctionProcess process, DateTimeOffset after)
    {
        if (process.Key == "Nightly")
        {
            return NightlySlots.Next(after).At;
        }
        if (process.Key == "CampaignSend")
        {
            // Hourly in UTC like the clock, and acts in the 8 o'clock hour Central only (ADR-118).
            for (var hour = Minute(after).AddMinutes(60 - Minute(after).Minute); ; hour = hour.AddHours(1))
            {
                if (NightlySlots.Local(hour).Hour == 8)
                {
                    return hour;
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
    private static HashSet<int> Field(string field, int count) =>
        field == "*" ? [.. Enumerable.Range(0, count)] : [.. field.Split(',').Select(int.Parse)];

    private static DateTimeOffset Minute(DateTimeOffset at) => new(at.UtcDateTime.Year, at.UtcDateTime.Month, at.UtcDateTime.Day, at.UtcDateTime.Hour, at.UtcDateTime.Minute, 0, TimeSpan.Zero);
}
// #endregion function-processes
