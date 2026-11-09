// The job hunt's funnel as the Admin tab grades it (ADR-109, the addendum on the funnel): the Function writes one
// line a Central day with six counts, the card adds up the days a window holds, and its tone is the calls booked
// over the newest seven days against two to three a week. Pure: the days go in, the totals and the tone come out;
// nothing here reads the clock.
using TheYard.Domain.Shared.Cards;

namespace TheYard.Domain.Features.Admin;

// #region funnel-rules
/// <summary>One Central day of the funnel, as the Function's line for that day gives it.</summary>
/// <param name="Day">The Central day, yyyy-MM-dd.</param>
/// <param name="Scanned">Postings the scans read.</param>
/// <param name="Ready">Postings that cleared the picker and were staged READY.</param>
/// <param name="Applied">Applications sent.</param>
/// <param name="Reached">People reached: invites, InMails, campaign sends and named leaders.</param>
/// <param name="Replied">Replies from a person, not a system.</param>
/// <param name="Booked">First calls booked.</param>
public sealed record FunnelDay(string Day, long Scanned, long Ready, long Applied, long Reached, long Replied, long Booked);

/// <summary>The funnel's totals and its one tone.</summary>
public static class FunnelRules
{
    /// <summary>The six counts in the order the line writes them and the card draws them.</summary>
    public static readonly IReadOnlyList<string> Counts = ["scanned", "ready", "applied", "reached", "replied", "booked"];

    /// <summary>The floor of the target, two to three first calls a week.</summary>
    public const int CallsAWeek = 2;

    /// <summary>The Function writes its line once a Central day.</summary>
    public const int CadenceSeconds = 86_400;

    /// <summary>Every count added across <paramref name="days"/>; zeros for none. A day written twice counts once, its later line.</summary>
    /// <param name="days">The days in the window, in the order the lines were written.</param>
    public static FunnelDay Sum(IEnumerable<FunnelDay> days)
    {
        var once = days.GroupBy(day => day.Day, StringComparer.Ordinal).Select(written => written.Last()).ToList();
        return new FunnelDay(
            once.Count == 0 ? "" : $"{once.Min(day => day.Day)} to {once.Max(day => day.Day)}",
            once.Sum(day => day.Scanned),
            once.Sum(day => day.Ready),
            once.Sum(day => day.Applied),
            once.Sum(day => day.Reached),
            once.Sum(day => day.Replied),
            once.Sum(day => day.Booked));
    }

    /// <summary>
    /// The card's tone from the newest seven days: unread with no line in them or a newest line three days old; red
    /// with no call booked; amber with one, under the floor; fine at two or more. A booked count outranks the line's
    /// age, as every card's reading does (CardTones.Of).
    /// </summary>
    /// <param name="week">The days of the newest seven that have a line.</param>
    /// <param name="newestAgeSeconds">How old the newest line is, or null when there is none.</param>
    public static CardTone Grade(IReadOnlyList<FunnelDay> week, long? newestAgeSeconds)
    {
        if (week.Count == 0)
        {
            return CardTone.Unread;
        }
        long booked = Sum(week).Booked;
        int held = booked >= CallsAWeek ? 0 : booked == 1 ? 1 : 2;
        return CardTones.Of([held], newestAgeSeconds, CadenceSeconds);
    }
}
// #endregion funnel-rules
