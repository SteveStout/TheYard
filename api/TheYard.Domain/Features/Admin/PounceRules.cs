// Time to first touch as the Admin tab judges it (ADR-109, the addendum on time to first touch): the Function writes
// process:PounceDay lines for the last seven Central days, many a day, each with the day's six counts at the moment
// it was written, and the newest line for a day is the truth for that day. The card shows the newest seven Central
// days, newest first, each judged against a median of 60 minutes. Pure: the lines and the moment go in, the week
// comes out; nothing here reads the clock.
namespace TheYard.Domain.Features.Admin;

// #region pounce-rules
/// <summary>One Central day of time to first touch, as the Function's newest line for that day gives it. Counts only.</summary>
/// <param name="Day">The Central day, yyyy-MM-dd.</param>
/// <param name="Caught">Roles that passed the code gate and went live that day.</param>
/// <param name="Touched">Of those, the ones a first message has reached.</param>
/// <param name="MedianMin">Minutes from going live to that first message, the median over the touched.</param>
/// <param name="WorstMin">The same minutes, the longest over the touched.</param>
/// <param name="Waiting">The ones no message has reached.</param>
/// <param name="OldestWaitMin">The longest of those waits when the line was written.</param>
public sealed record PounceDay(string Day, long Caught, long Touched, long MedianMin, long WorstMin, long Waiting, long OldestWaitMin);

/// <summary>Which line is a day's, the week the card shows, and the one judgement each day gets.</summary>
public static class PounceRules
{
    /// <summary>The target: a first message within an hour of a posting going live, 24/7.</summary>
    public const int TargetMinutes = 60;

    /// <summary>How many Central days the card shows.</summary>
    public const int WeekDays = 7;

    /// <summary>The judgements, as the wire writes them.</summary>
    public const string OnTarget = "on_target", Late = "late", Waiting = "waiting", Quiet = "quiet", Unread = "unread";

    /// <summary>
    /// The newest seven Central days, newest first, ending on the Central day of <paramref name="now"/>; each is the
    /// newest line written for it, or null when no line names it.
    /// </summary>
    /// <param name="lines">Every line read, each with when it was written, in any order.</param>
    /// <param name="now">The moment the week ends.</param>
    public static IReadOnlyList<(string Day, PounceDay? Line)> Week(IEnumerable<(DateTimeOffset At, PounceDay Day)> lines, DateTimeOffset now)
    {
        var newest = lines
            .GroupBy(line => line.Day.Day, StringComparer.Ordinal)
            .ToDictionary(day => day.Key, day => day.MaxBy(line => line.At).Day, StringComparer.Ordinal);
        var today = DateOnly.FromDateTime(NightlySlots.Local(now));
        return [.. Enumerable.Range(0, WeekDays)
            .Select(back => today.AddDays(-back).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Select(day => (day, newest.GetValueOrDefault(day)))];
    }

    /// <summary>
    /// The day's judgement: on_target when a role was touched and the median is 60 minutes or less, late when the
    /// median is over 60, waiting when nothing is touched and something waits, quiet when nothing was caught, and
    /// unread for a day with no line.
    /// </summary>
    /// <param name="day">The day's newest line, or null.</param>
    public static string Judge(PounceDay? day) => day switch
    {
        null => Unread,
        { Touched: > 0, MedianMin: <= TargetMinutes } => OnTarget,
        { Touched: > 0 } => Late,
        { Waiting: > 0 } => Waiting,
        _ => Quiet,
    };
}
// #endregion pounce-rules
