// The one tone a card on the Admin tab carries (ADR: The onion cut into vertical slices, the
// CardShell): the rail's dot, the header's chip, the dials' arcs and the summary rows all read it,
// so nothing computes it twice. Pure: the readings' tones and the newest reading's age go in, the
// tone comes out.
namespace TheYard.Domain;

// #region card-tone
/// <summary>A card's tone: fine, amber, red, or unread when its newest reading is too old to judge.</summary>
public enum CardTone
{
    /// <summary>Every reading inside its amber line.</summary>
    Fine,

    /// <summary>A reading over its amber line for the held window.</summary>
    Amber,

    /// <summary>A reading over its red line for the held window.</summary>
    Red,

    /// <summary>The newest reading is older than three of the card's reads, or there is none.</summary>
    Unread,
}

/// <summary>The rule that turns a card's readings into its one tone.</summary>
public static class CardTones
{
    /// <summary>How many of the card's reads the newest reading may be old before the card reads as unread.</summary>
    public const int ReadsBeforeUnread = 3;

    /// <summary>
    /// Red when any reading is over its red line for the held window; amber when any is over its
    /// amber line; unread when the newest reading is older than three of the card's reads (a
    /// minute's for a card read every minute, an hour's for one read hourly), or there is none;
    /// fine otherwise. A red or amber reading outranks its age: a card that was last read over
    /// its line says so until a fresher reading says otherwise.
    /// </summary>
    /// <param name="readings">Each reading's held state: 2 over red, 1 over amber, 0 inside.</param>
    /// <param name="ageSeconds">How old the newest reading is, or null when nothing is read.</param>
    /// <param name="cadenceSeconds">How often the card's source is read.</param>
    public static CardTone Of(IEnumerable<int> readings, long? ageSeconds, int cadenceSeconds)
    {
        int worst = readings.DefaultIfEmpty(0).Max();
        return worst >= 2 ? CardTone.Red
            : worst == 1 ? CardTone.Amber
            : ageSeconds is not { } age || age > (long)ReadsBeforeUnread * cadenceSeconds ? CardTone.Unread
            : CardTone.Fine;
    }

    /// <summary>The tone as the wire writes it.</summary>
    /// <param name="tone">The tone.</param>
    public static string Wire(CardTone tone) => tone switch
    {
        CardTone.Red => "red",
        CardTone.Amber => "amber",
        CardTone.Unread => "unread",
        _ => "fine",
    };
}
// #endregion card-tone
