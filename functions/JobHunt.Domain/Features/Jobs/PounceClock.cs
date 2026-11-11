namespace JobHunt.Domain.Features.Jobs;

/// <summary>A tracker row as the pounce's clock reads it: its seven cells and the instant it reached SQL (jobhunt.Tracker.WrittenAt).</summary>
/// <param name="Cells">Count, Person, Company, Communication, Date, Phone/Email, Notes.</param>
/// <param name="WrittenAt">When the row reached SQL.</param>
public sealed record TrackerTouch(IReadOnlyList<string> Cells, DateTimeOffset WrittenAt);

/// <summary>One campaign email that went out (campaign.EmailsSent with Shadow 0), with the name of the person its lead names.</summary>
/// <param name="Company">The company the email was for.</param>
/// <param name="To">The address it went to.</param>
/// <param name="ContactName">The lead's contact name.</param>
/// <param name="SentAt">When it was sent.</param>
public sealed record EmailTouch(string Company, string To, string ContactName, DateTimeOffset SentAt);

/// <summary>One Central day's cohort: the six counts of its process:PounceDay run line, by the names the Admin card reads.</summary>
/// <param name="Day">The Central day the roles went live on.</param>
/// <param name="Caught">Roles that passed and went live that day.</param>
/// <param name="Touched">Of them, touched.</param>
/// <param name="MedianMin">The median wait of the touched, whole minutes.</param>
/// <param name="WorstMin">The longest wait of the touched.</param>
/// <param name="Waiting">Of them, not touched.</param>
/// <param name="OldestWaitMin">The longest wait among the untouched, at the moment of the line.</param>
public sealed record PounceDay(DateOnly Day, long Caught, long Touched, long MedianMin, long WorstMin, long Waiting, long OldestWaitMin)
{
    /// <summary>The counts under the names the run line carries them by; pounce1110-s3's card reads these names.</summary>
    public IReadOnlyList<(string Name, long Value)> Named =>
        [("caught", Caught), ("touched", Touched), ("median_min", MedianMin), ("worst_min", WorstMin), ("waiting", Waiting), ("oldest_wait_min", OldestWaitMin)];
}

/// <summary>
/// The pounce's clock (pounce1110 step 2b, Steve's measure): minutes from a posting going live to the first message
/// reaching a hiring manager or VP. The live instant is the board's when it gives one no later than the first
/// sighting, else the sighting. A touch is a tracker row at the same company, written at or after the live instant and
/// dated no earlier than its Central day, that reached a person (TrackerRows.Channel) and names a leader seat; or a
/// campaign email to that company at or after it, to an address carrying the contact's own name. The earliest wins.
/// </summary>
public static class PounceClock
{
    /// <summary>The longest a passed role is watched for its first touch, in days.</summary>
    public const int WatchDays = 14;

    /// <summary>A role caught this many minutes or more after going live counts as late.</summary>
    public const int LateMinutes = 30;

    /// <summary>The live instant and whether the board gave it.</summary>
    public static (DateTimeOffset At, bool Known) LiveAt(DateTimeOffset? published, DateTimeOffset firstSeen) =>
        published is { } at && at <= firstSeen ? (at, true) : (firstSeen, false);

    /// <summary>Whole minutes from one instant to a later one, rounded down.</summary>
    public static long WholeMinutes(DateTimeOffset from, DateTimeOffset to) => (long)Math.Floor((to - from).TotalMinutes);

    /// <summary>Year-end budget: the role went live in the last 21 days and the calendar year it went live in has not ended.</summary>
    public static bool BudgetFlag(DateTimeOffset live, DateOnly today)
    {
        var day = DateOnly.FromDateTime(CentralClock.Local(live));
        return today.DayNumber - day.DayNumber is >= 0 and <= 21 && today.Year == day.Year;
    }

    /// <summary>The first touch of a role at the company that went live at the instant given, with its channel word, or null.</summary>
    public static (DateTimeOffset At, string Channel)? FirstTouch(string company, DateTimeOffset live, IEnumerable<TrackerTouch> rows, IEnumerable<EmailTouch> emails)
    {
        var liveDay = DateOnly.FromDateTime(CentralClock.Local(live));
        var tracker = rows
            .Where(r => r.Cells.Count >= 7 && r.WrittenAt >= live && ApplicationCap.Day(r.Cells[4]) is { } day && day >= liveDay)
            .Where(r => ApplicationCap.Same(r.Cells[2].Split(" (")[0], company) && PounceRules.IsLeader(r.Cells[1], r.Cells[2], r.Cells[6]))
            .Select(r => (r.WrittenAt, Channel: TrackerRows.Channel(r.Cells[6].TrimStart(), r.Cells[3])))
            .Where(t => t.Channel is not null)
            .Select(t => (At: t.WrittenAt, Channel: t.Channel!));
        var mail = emails
            .Where(e => e.SentAt >= live && ApplicationCap.Same(e.Company, company) && PounceRules.IsNamedAddress(e.To, e.ContactName))
            .Select(e => (At: e.SentAt, Channel: "campaign"));
        var all = tracker.Concat(mail).OrderBy(t => t.At).ToList();
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>One Central day's line over the passed roles: each role's live instant and its first touch, or null.</summary>
    public static PounceDay DayLine(DateOnly day, IEnumerable<(DateTimeOffset Live, DateTimeOffset? Touch)> roles, DateTimeOffset now)
    {
        var cohort = roles.Where(r => DateOnly.FromDateTime(CentralClock.Local(r.Live)) == day).ToList();
        var waits = cohort.Where(r => r.Touch is not null).Select(r => WholeMinutes(r.Live, r.Touch!.Value)).Order().ToList();
        var open = cohort.Where(r => r.Touch is null).Select(r => WholeMinutes(r.Live, now)).ToList();
        return new PounceDay(day, cohort.Count, waits.Count, Median(waits), waits.Count == 0 ? 0 : waits[^1], open.Count, open.Count == 0 ? 0 : open.Max());
    }

    /// <summary>The middle value, or for an even count the mean of the two middle values with a half rounded up; 0 for none.</summary>
    public static long Median(IReadOnlyList<long> sorted) => sorted.Count switch
    {
        0 => 0,
        var n when n % 2 == 1 => sorted[n / 2],
        var n => (sorted[(n / 2) - 1] + sorted[n / 2] + 1) / 2,
    };
}
