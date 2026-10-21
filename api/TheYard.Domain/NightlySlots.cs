// The nightly lane's slots in Central time, the one table both the Function that fires them and the
// Admin tab card that watches them read (ADR: The Nightly Function on the Admin tab). The Function's
// timer fires every hour on the hour in UTC, because Flex Consumption on Linux does not honour a
// time zone setting, and this decides what each hour is in America/Chicago: 4 AM is the reply lane,
// and midnight, 8 AM, noon, 4 PM and 8 PM are the records run. Pure: the moment goes in, the slot
// comes out; nothing here reads the clock. The Function compiles this same file by a link in its
// project, so the two can never keep different tables.
namespace TheYard.Domain;

// #region nightly-slots
/// <summary>The nightly lane's slots in Central time, and how a month of firings is judged against them.</summary>
public static class NightlySlots
{
    /// <summary>The 4 AM reply lane, by the name its dispatch carries.</summary>
    public const string Reply = "nightly";

    /// <summary>The four-hourly records run, by the name its dispatch carries.</summary>
    public const string Bookkeeping = "bookkeeping";

    /// <summary>The zone the slots are written in.</summary>
    public const string ZoneId = "America/Chicago";

    /// <summary>What an on-time slot, a late one and a missed one are called.</summary>
    public const string OnTime = "on time", Late = "late", Missed = "missed";

    /// <summary>How long after the hour a firing still counts as on time: the first five-minute bucket.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    /// <summary>The moment in Central wall-clock time.</summary>
    /// <param name="utc">The moment.</param>
    public static DateTime Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Central).DateTime;

    /// <summary>The run the hour of this moment starts, or null when the hour is not a slot.</summary>
    /// <param name="utc">The moment.</param>
    public static string? Slot(DateTimeOffset utc)
    {
        int hour = Local(utc).Hour;
        if (hour == 4)
        {
            return Reply;
        }
        return hour % 4 == 0 ? Bookkeeping : null;
    }

    /// <summary>
    /// Every whole UTC hour from <paramref name="from"/> (inclusive) to <paramref name="to"/>
    /// (exclusive) whose Central hour is a slot, oldest first, with the slot it is. Central's offset
    /// is always a whole number of hours, so a whole UTC hour is a whole Central hour.
    /// </summary>
    /// <param name="from">Where the window starts.</param>
    /// <param name="to">Where the window ends.</param>
    public static IReadOnlyList<(DateTimeOffset At, string Slot)> Due(DateTimeOffset from, DateTimeOffset to)
    {
        var due = new List<(DateTimeOffset At, string Slot)>();
        for (var hour = CeilingHour(from); hour < to; hour = hour.AddHours(1))
        {
            if (Slot(hour) is { } slot)
            {
                due.Add((hour, slot));
            }
        }
        return due;
    }

    /// <summary>The first slot hour strictly after <paramref name="after"/>, with the slot it is.</summary>
    /// <param name="after">The moment to look from.</param>
    public static (DateTimeOffset At, string Slot) Next(DateTimeOffset after)
    {
        // A slot comes at least every four hours, so this ends within a day.
        for (var hour = FloorHour(after).AddHours(1); ; hour = hour.AddHours(1))
        {
            string? slot = Slot(hour);
            if (slot is not null)
            {
                return (hour, slot);
            }
        }
    }

    /// <summary>
    /// What became of one slot: on time when a bucket that starts inside its first five minutes
    /// counted a firing; late when the first bucket that counted one starts later in the same hour;
    /// missed otherwise. The moment it fired comes back beside the word, null when it was missed.
    /// </summary>
    /// <param name="slot">The slot's hour, UTC.</param>
    /// <param name="executions">Buckets of firings: each bucket's start and how many it counted.</param>
    public static (string Outcome, DateTimeOffset? FiredAt) Judge(DateTimeOffset slot, IReadOnlyList<(DateTimeOffset At, double Count)> executions)
    {
        DateTimeOffset? first = null;
        foreach (var (at, count) in executions)
        {
            if (count > 0 && at >= slot && at < slot.AddHours(1) && (first is null || at < first))
            {
                first = at;
            }
        }
        if (first is not { } fired)
        {
            return (Missed, null);
        }
        return fired < slot + Grace ? (OnTime, fired) : (Late, fired);
    }

    /// <summary>How many of the due slots fired on time, late, or not at all.</summary>
    /// <param name="due">The slots that were due, from <see cref="Due"/>.</param>
    /// <param name="executions">Buckets of firings: each bucket's start and how many it counted.</param>
    public static SlotTally Tally(IReadOnlyList<(DateTimeOffset At, string Slot)> due, IReadOnlyList<(DateTimeOffset At, double Count)> executions)
    {
        int onTime = 0, late = 0, missed = 0;
        foreach (var (at, _) in due)
        {
            string outcome = Judge(at, executions).Outcome;
            if (outcome == OnTime)
            {
                onTime++;
            }
            else if (outcome == Late)
            {
                late++;
            }
            else
            {
                missed++;
            }
        }
        return new SlotTally(due.Count, onTime, late, missed);
    }

    /// <summary>The UTC moment the Central day holding <paramref name="utc"/> began; midnight is never skipped or repeated in Chicago.</summary>
    /// <param name="utc">The moment.</param>
    public static DateTimeOffset StartOfCentralDay(DateTimeOffset utc)
    {
        var midnight = Local(utc).Date;
        return new DateTimeOffset(midnight, Central.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static DateTimeOffset FloorHour(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset CeilingHour(DateTimeOffset at)
    {
        var floor = FloorHour(at);
        return floor == at.ToUniversalTime() ? floor : floor.AddHours(1);
    }
}

/// <summary>A month of slots, counted: how many were due, and how many fired on time, late or not at all.</summary>
/// <param name="Due">The slots that were due.</param>
/// <param name="OnTime">The ones that fired inside their first five minutes.</param>
/// <param name="Late">The ones that fired later in their hour.</param>
/// <param name="Missed">The ones that did not fire in their hour.</param>
public sealed record SlotTally(int Due, int OnTime, int Late, int Missed)
{
    /// <summary>The share on time, in per cent to one place; null when nothing was due.</summary>
    public double? OnTimePercent => Due == 0 ? null : Math.Round(100.0 * OnTime / Due, 1);
}
// #endregion nightly-slots
