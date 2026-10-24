// The windows every card under Cloud infrastructure offers, shared by the plan's slice and the
// resource cards' slice so neither reaches into the other (ADR: The onion cut into vertical slices).
using TheYard.Application.Shared.History;

namespace TheYard.Application.Shared.Cards;

/// <summary>The windows the Admin tab's cloud cards offer, and the day for anything else.</summary>
public static class CardWindows
{
    /// <summary>The window by name: 24h, 7d or 30d, and 24h for any other word or none.</summary>
    /// <param name="window">What the page asked for.</param>
    public static string Of(string? window) => MachineWindows.Parse(window) is { } chosen ? chosen.Name : "24h";
}
