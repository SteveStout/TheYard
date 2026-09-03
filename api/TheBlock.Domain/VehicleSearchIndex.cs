using TheBlock.Data;

namespace TheBlock.Domain;

/// <summary>
/// The lowercase text a free-text query is matched against, computed once per
/// vehicle instead of once per vehicle per request.
///
/// The old path built this string inside the filter loop, so a search across
/// the synthetic 100,000-row dataset interpolated nine fields and allocated a
/// lowercase copy a hundred thousand times for a query the user typed once.
/// Nothing in the text changes after startup, which makes it exactly the kind
/// of work that belongs in a table built once (ADR: Search index).
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
