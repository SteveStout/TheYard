using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>One page of search results plus the total match count.</summary>
/// <param name="Total">How many vehicles match the search across all pages.</param>
/// <param name="Vehicles">The vehicles on the requested page.</param>
public sealed record SearchResult(int Total, IReadOnlyList<Vehicle> Vehicles);

/// <summary>Distinct dropdown values, one list per filterable field.</summary>
/// <param name="Makes">Every distinct make in the inventory.</param>
/// <param name="BodyStyles">Every distinct body style in the inventory.</param>
/// <param name="TitleStatuses">Every distinct title status in the inventory.</param>
/// <param name="Provinces">Every distinct province a vehicle sits in.</param>
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
    // The host starts WarmAsync beside the server (Composition/Startup.cs) and the
    // pipeline awaits it before any store-bound endpoint runs (Warmth, Stores.cs), so on a
    // running site the accessors below read a task that has already finished and
    // never block. A caller that skips the warm-up, which is what a unit test
    // over an in-memory source does, blocks on a task that a memory source has
    // already completed, which is a wait of no time. The only way to block a
    // thread here for real is to skip the warm-up against a store that has to
    // go over the network, and the host does not (ADR: The ports learn to wait).
    //
    // A load that failed is not kept. A Lazy would keep a faulted task forever, so
    // a store that was unreachable for one second at startup would answer every
    // request until the next roll with that second's exception. Here the next
    // caller after a failure starts a fresh load; a load in flight or finished is
    // shared, and the lock is what makes the start single.
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
        if (sort == VehicleSort.EndingSoonest)
        {
            return SearchEndingSoonest(filter, clock, limit, offset, overlay);
        }

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
        var matched = source.Where(matches).ToList();
        // #endregion search
        // #region page
        // The count needs every match and the page needs a hundred of them.
        // Sorting every match and then taking the page would be a full sort
        // of a hundred thousand rows on every unfiltered listing, and on the
        // plan's one shared core that is the slowest read the site has (ADR: The
        // Admin tab, as a product, the addendum on the first amber tile). Skip and Take straight off the ordering let the
        // runtime sort only as far as the page: the same rows in the same
        // order, ties included, because the ordering is still the stable one.
        var page = VehicleOrdering.Sort(matched, sort, clock).Skip(offset).Take(limit).ToList();
        return new SearchResult(matched.Count, page);
        // #endregion page
    }

    // #region ending-soonest
    // The default listing, and the one every visitor opens. Sorting all hundred
    // thousand vehicles by their auction window on every request was most of its
    // time: measured on the build machine, 90 to 100 ms against 35 to 45 ms for the
    // same page sorted by price, and several times that on the plan's shared core.
    // The windows are fixed for the day, so the order is read off a ScheduleOrder
    // built once per anchor and the page stops at its last row (ADR: The search
    // index, the addendum on the schedule order). The total still counts every
    // match, so the filter still runs down every row.
    private ScheduleOrder? _schedule;
    private IReadOnlyList<Vehicle>? _scheduledFor;

    private ScheduleOrder ScheduleFor(IReadOnlyList<Vehicle> all, long anchorMs)
    {
        // Built again when the day's anchor moves or the catalogue was loaded again. Two
        // requests that arrive together at midnight may both build it, and either result
        // is the same order, so no lock is needed.
        ScheduleOrder? current = Volatile.Read(ref _schedule);
        if (current is not null && current.AnchorMs == anchorMs && ReferenceEquals(Volatile.Read(ref _scheduledFor), all))
        {
            return current;
        }

        var built = new ScheduleOrder(all, anchorMs);
        Volatile.Write(ref _scheduledFor, all);
        Volatile.Write(ref _schedule, built);
        return built;
    }

    private SearchResult SearchEndingSoonest(VehicleFilter filter, AuctionClock clock, int limit, int offset, Func<Vehicle, Vehicle>? overlay)
    {
        var all = GetAll();
        var matches = filter.Compile(clock, Inventory.Index);
        var kept = new Vehicle?[all.Count];
        int total = 0;
        for (int index = 0; index < all.Count; index++)
        {
            Vehicle vehicle = overlay is null ? all[index] : overlay(all[index]);
            if (matches(vehicle))
            {
                kept[index] = vehicle;
                total++;
            }
        }

        var page = new List<Vehicle>(Math.Max(0, Math.Min(limit, total - offset)));
        if (limit <= 0)
        {
            return new SearchResult(total, page);
        }

        int skipped = 0;
        ScheduleFor(all, clock.AnchorMs).Visit(clock.NowMs, index =>
        {
            if (kept[index] is not { } vehicle)
            {
                return true;
            }

            if (skipped < offset)
            {
                skipped++;
                return true;
            }

            page.Add(vehicle);
            return page.Count < limit;
        });

        return new SearchResult(total, page);
    }
    // #endregion ending-soonest

    // #region facets
    /// <summary>
    /// Distinct values feeding the UI's filter dropdowns, sorted. Built once
    /// with the catalogue and handed back by reference, because walking all
    /// hundred thousand vehicles four times on every request cost 12 ms on the
    /// live container for an answer that cannot change after load
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
