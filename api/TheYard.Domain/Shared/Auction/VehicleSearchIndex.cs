using TheYard.Data;

namespace TheYard.Domain.Shared.Auction;

/// <summary>
/// The lowercase text a free-text query is matched against, computed once per
/// vehicle instead of once per vehicle per request.
///
/// Building this string inside the filter loop would interpolate nine fields
/// and allocate a lowercase copy a hundred thousand times per search of the
/// synthetic dataset, for a query the user typed once.
/// Nothing in the text changes after startup, which makes it exactly the kind
/// of work that belongs in a table built once (ADR: The search index).
///
/// The auction status is deliberately NOT in here. It is derived from the
/// clock, so it is the one searchable value that changes without the data
/// changing; <see cref="VehicleFilter"/> checks it separately, and only for
/// the tokens the static text did not already satisfy.
/// </summary>
public sealed class VehicleSearchIndex
{
    private readonly Dictionary<string, string> _byId;

    public VehicleSearchIndex(IEnumerable<Vehicle> vehicles) =>
        _byId = vehicles.ToDictionary(vehicle => vehicle.Id, TextFor, StringComparer.Ordinal);

    public int Count => _byId.Count;

    // #region text
    /// <summary>
    /// Every searchable field that does not depend on the clock: identity
    /// (year, make, model, trim) plus the fields the dropdown filters cover
    /// and the city. Single-spaced and lowercased once, because the query
    /// tokens are lowercased once too and a token can never contain a space.
    /// </summary>
    public static string TextFor(Vehicle vehicle) =>
        string.Join(' ',
            vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Trim,
            vehicle.BodyStyle, vehicle.TitleStatus, vehicle.Province, vehicle.City)
        .ToLowerInvariant();
    // #endregion text

    // #region words
    /// <summary>
    /// The shortest typed word looked for as the start of a word. "hon" finds Honda; a shorter
    /// token, "4x" or "ex", is looked for anywhere in the text.
    /// The stores answer a word from a table of words, one row per word of the text, which an
    /// index can seek; a token shorter than this is a scan on either store (ADR: The store does
    /// the work, the addendum on a typed word as a seek).
    /// </summary>
    public const int WordLength = 3;

    /// <summary>The distinct words of a searchable text, in the order they first appear.</summary>
    public static IReadOnlyList<string> WordsOf(string text) =>
        [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Whether the text holds the token: a token of <see cref="WordLength"/> or more as the start of
    /// one of its words, a shorter one anywhere. The text is single-spaced, so a word starts at the
    /// text's start or after a space. Every store and the in-memory filters ask this one question.
    /// </summary>
    public static bool Holds(string text, string token) =>
        token.Length < WordLength
            ? text.Contains(token, StringComparison.Ordinal)
            : text.StartsWith(token, StringComparison.Ordinal) || text.Contains(" " + token, StringComparison.Ordinal);
    // #endregion words

    // #region lookup
    /// <summary>
    /// The indexed text for a vehicle, or a freshly computed one when the
    /// vehicle is not in the index. The fallback matters: the bid overlay
    /// rebuilds each vehicle with `with`, and a test can hand the filter a
    /// vehicle this index never saw. Keying by id rather than by reference is
    /// what makes the overlay free here, because an overlay only rewrites the
    /// bid figures and never the text.
    /// </summary>
    public string For(Vehicle vehicle) =>
        _byId.TryGetValue(vehicle.Id, out string? text) ? text : TextFor(vehicle);
    // #endregion lookup
}
