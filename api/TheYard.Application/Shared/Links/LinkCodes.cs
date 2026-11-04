// What a tagged link's code is (ADR: Tagged links, who opened what), the one rule every place that reads a
// code asks: the Links slice's opens, the shared request recorder, and the review route's reach (ADR: Ten
// minutes, seven stops). A rule two slices read is a Shared concern, so it sits here and not in either.
using System.Text.RegularExpressions;

namespace TheYard.Application.Shared.Links;

// #region link-codes
/// <summary>What a code is: six lower-case letters or digits, the shape make_ref.py mints and nothing a person could be named by.</summary>
public static partial class LinkCodes
{
    /// <summary>The query key a tagged link carries the code under.</summary>
    public const string QueryKey = "r";

    /// <summary>True for a string that is a code.</summary>
    /// <param name="value">The query value, or null.</param>
    public static bool IsOne(string? value) => value is not null && Shape().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]{6}$")]
    private static partial Regex Shape();
}
// #endregion link-codes
