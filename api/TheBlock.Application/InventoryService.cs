using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Application;

/// <summary>One page of search results plus the total match count.</summary>
public sealed record SearchResult(int Total, IReadOnlyList<Vehicle> Vehicles);

/// <summary>Distinct dropdown values, one list per filterable field.</summary>
public sealed record InventoryFacets(
    IReadOnlyList<string> Makes,
    IReadOnlyList<string> BodyStyles,
    IReadOnlyList<string> TitleStatuses,
    IReadOnlyList<string> Provinces);

/// <summary>
/// The read-only inventory use case: loads the dataset once, rewrites each
/// vehicle's images to gallery picks served under <paramref name="imagePathPrefix"/>,
/// and answers list and by-id lookups. Vehicles whose body style has no photo
/// pool keep the images the dataset carries.
/// </summary>
public sealed class InventoryService(
    IVehicleSource vehicleSource,
    IPhotoManifestSource manifestSource,
    string imagePathPrefix = "/api/images")
{
    private readonly Lazy<(IReadOnlyList<Vehicle> All, IReadOnlyDictionary<string, Vehicle> ById)> _inventory =
        new(() => Build(vehicleSource, manifestSource, imagePathPrefix));

    public IReadOnlyList<Vehicle> GetAll() => _inventory.Value.All;

    /// <summary>
    /// Vehicles matching <paramref name="filter"/> (statuses derived from
    /// <paramref name="clock"/>), ordered by <paramref name="sort"/>, paged by
    /// <paramref name="limit"/>/<paramref name="offset"/>. Total counts every
    /// match. <paramref name="overlay"/> (the buyer's bids) is applied BEFORE
    /// filtering, so price bounds see the same figures the UI displays.
    /// </summary>
    public SearchResult Search(
        VehicleFilter filter,
        AuctionClock clock,
        VehicleSort sort = VehicleSort.EndingSoonest,
        int limit = int.MaxValue,
        int offset = 0,
        Func<Vehicle, Vehicle>? overlay = null)
    {
        IEnumerable<Vehicle> source = GetAll();
        if (overlay is not null)
        {
            source = source.Select(overlay);
        }
        var matched = source.Where(vehicle => filter.Matches(vehicle, clock));
        var ordered = VehicleOrdering.Sort(matched, sort, clock).ToList();
        return new SearchResult(ordered.Count, ordered.Skip(offset).Take(limit).ToList());
    }

    /// <summary>Distinct values feeding the UI's filter dropdowns, sorted.</summary>
    public InventoryFacets Facets()
    {
        var vehicles = GetAll();
        return new InventoryFacets(
            Distinct(vehicles, v => v.Make),
            Distinct(vehicles, v => v.BodyStyle),
            Distinct(vehicles, v => v.TitleStatus),
            Distinct(vehicles, v => v.Province));
    }

    private static IReadOnlyList<string> Distinct(
        IReadOnlyList<Vehicle> vehicles,
        Func<Vehicle, string> field) =>
        vehicles.Select(field).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();

    public Vehicle? GetById(string id) =>
        _inventory.Value.ById.TryGetValue(id, out var vehicle) ? vehicle : null;

    private static (IReadOnlyList<Vehicle>, IReadOnlyDictionary<string, Vehicle>) Build(
        IVehicleSource vehicleSource,
        IPhotoManifestSource manifestSource,
        string imagePathPrefix)
    {
        // Key pools by lowercase style — PhotoGallery looks them up lowercased,
        // so a capitalized style in the manifest must not silently miss.
        var pools = manifestSource
            .Load()
            .GroupBy(photo => photo.Style.ToLowerInvariant())
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PhotoEntry>)group.ToList());

        var vehicles = vehicleSource
            .Load()
            .Select(vehicle =>
            {
                var photos = PhotoGallery.SelectPhotos(vehicle.Id, vehicle.Make, vehicle.BodyStyle, pools);
                return photos.Count == 0
                    ? vehicle
                    : vehicle with { Images = photos.Select(file => $"{imagePathPrefix}/{file}").ToList() };
            })
            .ToList();

        return (vehicles, vehicles.ToDictionary(vehicle => vehicle.Id));
    }
}
