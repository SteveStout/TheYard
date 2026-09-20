using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

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
    // #region warm
    // One load, shared by every caller, started by whoever asks first. The
    // task is what is shared rather than the result, so two callers arriving
    // together wait on the same load instead of running two.
    //
    // The host awaits WarmAsync before it serves anything (Program.cs), so on a
    // running site the accessors below read a task that finished at startup and
    // never block. A caller that skips the warm-up, which is what a unit test
    // over an in-memory source does, blocks on a task that a memory source has
    // already completed, which is a wait of no time. The only way to block a
    // thread here for real is to skip the warm-up against a store that has to
    // go over the network, and the host does not (ADR: The ports learn to wait).
    //
    // A load that failed is not kept. This was a Lazy once, and a Lazy keeps a
    // faulted task forever, so a store that was unreachable for one second at
    // startup would have answered every request until the next roll with that
    // second's exception while the host said its first visitor would try again
    // (ADR: The ports learn to wait, addendum). Now the next caller after a
    // failure starts a fresh load; a load in flight or finished is shared as
    // before, and the lock is what makes the start single.
    private readonly object _warmGate = new();
    private Task<(IReadOnlyList<Vehicle> All, IReadOnlyDictionary<string, Vehicle> ById, VehicleSearchIndex Index, InventoryFacets Facets)>? _inventory;

    private Task<(IReadOnlyList<Vehicle> All, IReadOnlyDictionary<string, Vehicle> ById, VehicleSearchIndex Index, InventoryFacets Facets)> InventoryTask()
    {
        lock (_warmGate)
        {
            if (_inventory is null || _inventory.IsFaulted || _inventory.IsCanceled)
            {
                _inventory = BuildAsync(vehicleSource, manifestSource, imagePathPrefix);
            }
            return _inventory;
        }
    }

    /// <summary>Load the catalogue now, so the first visitor does not pay for it.</summary>
    public Task WarmAsync() => InventoryTask();

    /// <summary>Whether the catalogue has been loaded, which the host asserts before serving.</summary>
    public bool IsWarm => _inventory is { IsCompletedSuccessfully: true };

    // #region let-go
    // A catalogue can be let go again. One process holds a catalogue per
    // store, a hundred thousand vehicles each, and on a machine two sites
    // share there is room for the one a site serves and not, for long, for
    // the one it does not (ADR: One plan, two sites). So the store a site does
    // not serve keeps its catalogue while somebody is using it and gives it
    // back when nobody has for a while; the next request for it loads it
    // again, awaited, exactly as a cold store's first request always has.
    //
    // Both halves are under the gate the load is started under, and that is
    // what makes it safe. A request touches before it checks for warmth: if
    // the touch lands first, the catalogue is no longer idle and stays; if
    // the release lands first, the request finds a cold store and awaits the
    // load. There is no order in which a request is handed a catalogue that
    // is about to disappear. A request already holding the lists keeps them:
    // they are immutable, and letting go only drops this service's reference.
    private DateTimeOffset _lastTouched = DateTimeOffset.MinValue;

    /// <summary>Says the catalogue is in use as of <paramref name="now"/>. Called once per request, before the warmth check.</summary>
    public void Touch(DateTimeOffset now)
    {
        lock (_warmGate)
        {
            if (now > _lastTouched)
            {
                _lastTouched = now;
            }
        }
    }

    /// <summary>
    /// Lets a loaded catalogue go when nothing has touched it for
    /// <paramref name="idle"/>. False, and nothing happens, when it is not
    /// loaded, still loading, or was touched more recently than that. A load
    /// in flight is never dropped: somebody is waiting on it.
    /// </summary>
    public bool ReleaseIfIdle(TimeSpan idle, DateTimeOffset now)
    {
        lock (_warmGate)
        {
            if (_inventory is not { IsCompletedSuccessfully: true } || now - _lastTouched < idle)
            {
                return false;
            }

            _inventory = null;
            return true;
        }
    }
    // #endregion let-go

    private (IReadOnlyList<Vehicle> All, IReadOnlyDictionary<string, Vehicle> ById, VehicleSearchIndex Index, InventoryFacets Facets) Inventory =>
        InventoryTask().GetAwaiter().GetResult();
    // #endregion warm

    public IReadOnlyList<Vehicle> GetAll() => Inventory.All;

    /// <summary>
    /// The searchable text for the loaded dataset, built with it. Exposed
    /// because the thing worth asserting about an index is that it covers the
    /// dataset it was built from (ADR: The search index).
    /// </summary>
    public VehicleSearchIndex SearchIndex => Inventory.Index;

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
        // #region search
        // Compile the filter once, then run the predicate down the rows. Both
        // halves of the free-text comparison are precomputed by this point: the
        // query's tokens by Compile, each vehicle's searchable text by the index
        // built at load (ADR: The search index). What is left per row is a
        // dictionary lookup and a substring test.
        var matches = filter.Compile(clock, Inventory.Index);
        var matched = source.Where(matches);
        // #endregion search
        var ordered = VehicleOrdering.Sort(matched, sort, clock).ToList();
        return new SearchResult(ordered.Count, ordered.Skip(offset).Take(limit).ToList());
    }

    // #region facets
    /// <summary>
    /// Distinct values feeding the UI's filter dropdowns, sorted. Built once
    /// with the catalogue and handed back by reference: until 1.0.0.140 this
    /// walked all hundred thousand vehicles four times on every request, 12 ms
    /// on the live container, for an answer that cannot change after load
    /// (ADR: The search index, addendum).
    /// </summary>
    public InventoryFacets Facets() => Inventory.Facets;

    private static InventoryFacets BuildFacets(IReadOnlyList<Vehicle> vehicles) =>
        new(
            Distinct(vehicles, v => v.Make),
            Distinct(vehicles, v => v.BodyStyle),
            Distinct(vehicles, v => v.TitleStatus),
            Distinct(vehicles, v => v.Province));

    private static IReadOnlyList<string> Distinct(
        IReadOnlyList<Vehicle> vehicles,
        Func<Vehicle, string> field) =>
        vehicles.Select(field).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
    // #endregion facets

    public Vehicle? GetById(string id) =>
        Inventory.ById.TryGetValue(id, out var vehicle) ? vehicle : null;

    private static async Task<(IReadOnlyList<Vehicle>, IReadOnlyDictionary<string, Vehicle>, VehicleSearchIndex, InventoryFacets)> BuildAsync(
        IVehicleSource vehicleSource,
        IPhotoManifestSource manifestSource,
        string imagePathPrefix)
    {
        // Key pools by lowercase style, because PhotoGallery looks them up lowercased,
        // so a capitalized style in the manifest must not silently miss.
        var pools = (await manifestSource.LoadAsync())
            .GroupBy(photo => photo.Style.ToLowerInvariant())
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PhotoEntry>)group.ToList());

        var vehicles = (await vehicleSource.LoadAsync())
            .Select(vehicle =>
            {
                var photos = PhotoGallery.SelectPhotos(vehicle.Id, vehicle.Make, vehicle.BodyStyle, pools);
                return photos.Count == 0
                    ? vehicle
                    : vehicle with { Images = photos.Select(file => $"{imagePathPrefix}/{file}").ToList() };
            })
            .ToList();

        // The index and the facets are built here, with the dictionary, for the
        // same reason the dictionary is: the work is identical for every request
        // that follows, and after this point the vehicles never change (ADR: The
        // search index).
        return (
            vehicles,
            vehicles.ToDictionary(vehicle => vehicle.Id),
            new VehicleSearchIndex(vehicles),
            BuildFacets(vehicles));
    }
}
