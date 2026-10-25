// Who a visitor was: a person, a scanner or crawler, or the site reading itself. One rule, here, read by
// both writers that count visitors: the Activity card's visitor-days and the all-time count of people who
// have visited (ADR-116: People who have visited, counted once), so the two surfaces can never disagree.
namespace TheYard.Domain.Shared.Visitors;

// #region visitor-kind
/// <summary>What a visitor was, by how its requests read.</summary>
public enum VisitorKind
{
    /// <summary>A person: at least one request that did not look like a bot.</summary>
    People,

    /// <summary>A scanner or crawler: every request looked like a bot.</summary>
    Scanners,

    /// <summary>The site's own tools reading it.</summary>
    Self,
}

/// <summary>The rule that names a visitor's kind, and which of two readings of the same visitor wins.</summary>
public static class VisitorKinds
{
    /// <summary>
    /// The kind of a visitor that made <paramref name="requests"/> requests of which <paramref name="bots"/> looked
    /// like a bot: the site itself when the network is the site's own, a scanner when every request looked like a
    /// bot, a person otherwise. A fetcher that previews a link is a bot by its agent, so it is never a person
    /// (ADR-108's line).
    /// </summary>
    public static VisitorKind Of(bool self, int requests, int bots) =>
        self ? VisitorKind.Self : bots >= requests ? VisitorKind.Scanners : VisitorKind.People;

    /// <summary>Two readings of one visitor: the site itself wins, then a person, then a scanner.</summary>
    public static VisitorKind Stronger(VisitorKind a, VisitorKind b) =>
        a == VisitorKind.Self || b == VisitorKind.Self ? VisitorKind.Self
        : a == VisitorKind.People || b == VisitorKind.People ? VisitorKind.People
        : VisitorKind.Scanners;

    /// <summary>The kind as it is kept: "person", "scanner" or "self".</summary>
    public static string Word(VisitorKind kind) => kind switch
    {
        VisitorKind.People => "person",
        VisitorKind.Scanners => "scanner",
        _ => "self",
    };

    /// <summary>The kind a kept word names; anything unknown reads as a scanner, so it is never counted as a person.</summary>
    public static VisitorKind FromWord(string? word) => word switch
    {
        "person" => VisitorKind.People,
        "self" => VisitorKind.Self,
        _ => VisitorKind.Scanners,
    };
}
// #endregion visitor-kind
