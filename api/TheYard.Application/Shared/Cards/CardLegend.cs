// What each colour on a Cloud infrastructure card means, in the owner's words (ADR-102, the addendum on what a colour
// means): one line every card carries under its title, served here so the page never types it and every card says the
// same. The page draws each entry's dot in the site's own colour for its tone.
using TheYard.Domain.Shared.Cards;

namespace TheYard.Application.Shared.Cards;

// #region card-legend
/// <summary>The colour legend every card under Cloud infrastructure carries, one entry a tone.</summary>
public static class CardLegend
{
    /// <summary>
    /// The four entries in the order the card prints them: green, amber, red, gray. The words are the owner's ruling on
    /// green and amber (ADR-102, the addendum on what a colour means), with red and gray in the same voice.
    /// </summary>
    public static readonly IReadOnlyList<CloudLegendEntry> Entries =
    [
        new(CardTones.Wire(CardTone.Fine), "Green", "working as expected."),
        new(CardTones.Wire(CardTone.Amber), "Amber", "worth researching."),
        new(CardTones.Wire(CardTone.Red), "Red", "needs action now."),
        new(CardTones.Wire(CardTone.Unread), "Gray", "no reading yet, such as the first minutes after a release."),
    ];
}

/// <summary>One colour of the legend: the tone its dot is drawn in, the colour's name and what it means.</summary>
/// <param name="Tone">The card tone as the wire writes it: fine, amber, red or unread.</param>
/// <param name="Name">The colour's name, printed in bold: "Green".</param>
/// <param name="Words">What the colour means, after the name: "working as expected.".</param>
public sealed record CloudLegendEntry([property: System.ComponentModel.Description("fine, amber, red or unread")] string Tone, string Name, string Words);
// #endregion card-legend
