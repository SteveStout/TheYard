using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

// #region fakes
// Fakes at the ports. The `file` modifier keeps these classes private to this file; each stands in
// for an adapter, so the service is tested with no filesystem and no store, and the counts prove
// what was read and how often.
file sealed class FakeVehicles(params Vehicle[] vehicles) : IVehicleSource
{
    public int LoadCalls { get; private set; }

    public Task<IReadOnlyList<Vehicle>> LoadAsync()
    {
        LoadCalls++;
        return Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
    }
}

file sealed class FakeManifest(params PhotoEntry[] photos) : IPhotoManifestSource
{
    public Task<IReadOnlyList<PhotoEntry>> LoadAsync() => Task.FromResult<IReadOnlyList<PhotoEntry>>(photos);
}

/// <summary>A source that is down for its first call and up after: the store at startup, sometimes.</summary>
file sealed class FlakyVehicles(params Vehicle[] vehicles) : IVehicleSource
{
    public int LoadCalls { get; private set; }

    public Task<IReadOnlyList<Vehicle>> LoadAsync()
    {
        LoadCalls++;
        return LoadCalls == 1
            ? Task.FromException<IReadOnlyList<Vehicle>>(new IOException("the store is not answering"))
            : Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
    }
}

/// <summary>
/// A store's catalogue, answered by the in-memory one behind it so the answers are right, and
/// counted, so a test can say what the service asked the store for. Not a MemoryCatalogue itself,
/// so the service treats it as a store: through its caches, and holding nothing.
/// </summary>
file sealed class CountingStore(IReadOnlyList<Vehicle> vehicles, int filled, CatalogueFillPace pace) : IVehicleCatalogue
{
    private readonly MemoryCatalogue _inner = new(vehicles);

    public int Filled { get; private set; } = filled;

    public int Pages { get; private set; }

    public int Counts { get; private set; }

    public int Written { get; private set; }

    /// <summary>How many counts and pages were asked with ids for the store to leave out.</summary>
    public int AskedToExclude { get; private set; }

    public int Target => vehicles.Count;

    public CatalogueFillPace Pace => pace;

    public Task<int> FilledAsync() => Task.FromResult(Filled);

    public Task FillAsync(IReadOnlyList<CatalogueEntry> batch)
    {
        Written += batch.Count;
        Filled = Math.Max(Filled, batch.Max(entry => entry.Seq) + 1);
        return Task.CompletedTask;
    }

    public Task<SegmentCounts> CountAsync(CatalogueFilter filter)
    {
        Counts++;
        AskedToExclude += filter.Exclude.Count > 0 ? 1 : 0;
        return _inner.CountAsync(filter);
    }

    public Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        Pages++;
        AskedToExclude += filter.Exclude.Count > 0 ? 1 : 0;
        return _inner.PageAsync(filter, order, skip, take);
    }

    public Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids) => _inner.FindAsync(ids);

    public Task<InventoryFacets> FacetsAsync() => _inner.FacetsAsync();
}
// #endregion fakes

/// <summary>Holds the inventory service: image rewriting, retrying a failed warm-up, lookup by id, reading once, filtered and paged search, the overlay's merge, facets, and where the catalogue is held.</summary>
public class InventoryServiceTests
{
    private static readonly AuctionClock Noon = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    private static readonly CatalogueFillPace AllAtWarmUp = new(100, TimeSpan.Zero, int.MaxValue);

    [Fact]
    public async Task Rewrites_images_to_the_configured_prefix()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(bodyStyle: "SUV")),
            new FakeManifest(TestData.SuvPool),
            "/api/images");

        var vehicle = Assert.Single((await service.SearchAsync(new VehicleFilter(), Noon)).Vehicles);
        Assert.Equal(PhotoGallery.GallerySize, vehicle.Images.Count);
        Assert.All(vehicle.Images, url => Assert.StartsWith("/api/images/suv-", url));
    }

    /// <summary>
    /// A warm-up that failed is tried again by the next caller (ADR: The ports learn to wait,
    /// addendum). The Lazy this replaced kept the first failure for the life of the process.
    /// </summary>
    [Fact]
    public async Task A_load_that_failed_is_tried_again_by_the_next_caller()
    {
        var source = new FlakyVehicles(TestData.Vehicle(id: "v-1"));
        var service = new InventoryService(source, new FakeManifest(TestData.SuvPool));

        await Assert.ThrowsAsync<IOException>(() => service.WarmAsync());
        Assert.False(service.IsWarm);

        await service.WarmAsync();
        Assert.True(service.IsWarm);
        Assert.Equal("v-1", (await service.FindAsync("v-1"))?.Id);
        Assert.Equal(2, source.LoadCalls);
    }

    [Fact]
    public async Task Keeps_dataset_images_when_the_body_style_has_no_pool()
    {
        var original = TestData.Vehicle(bodyStyle: "van", images: ["https://placehold.co/keep-me"]);
        var service = new InventoryService(new FakeVehicles(original), new FakeManifest(TestData.SuvPool));

        Assert.Equal(["https://placehold.co/keep-me"], (await service.FindAsync(original.Id))!.Images);
    }

    [Fact]
    public async Task Finds_vehicles_by_id_and_returns_null_for_unknown_ids()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(id: "v-1"), TestData.Vehicle(id: "v-2")),
            new FakeManifest(TestData.SuvPool));

        Assert.Equal("v-2", (await service.FindAsync("v-2"))?.Id);
        Assert.Null(await service.FindAsync("nope"));
    }

    [Fact]
    public async Task Loads_the_sources_once_across_calls()
    {
        var vehicles = new FakeVehicles(TestData.Vehicle());
        var service = new InventoryService(vehicles, new FakeManifest(TestData.SuvPool));

        await service.SearchAsync(new VehicleFilter(), Noon);
        await service.FindAsync("test-id");
        await service.SearchAsync(new VehicleFilter(), Noon);

        Assert.Equal(1, vehicles.LoadCalls);
    }

    [Fact]
    public async Task Search_applies_the_filter_and_an_empty_filter_returns_everything()
    {
        var service = new InventoryService(
            new FakeVehicles(
                TestData.Vehicle(id: "ford", make: "Ford"),
                TestData.Vehicle(id: "kia", make: "Kia")),
            new FakeManifest(TestData.SuvPool));

        var fords = await service.SearchAsync(new VehicleFilter { Make = "ford" }, Noon);
        Assert.Equal(1, fords.Total);
        Assert.Equal(["ford"], fords.Vehicles.Select(v => v.Id));
        Assert.Equal(2, (await service.SearchAsync(new VehicleFilter(), Noon)).Total);
        Assert.Equal(0, (await service.SearchAsync(new VehicleFilter { Make = "Lada" }, Noon)).Total);
    }

    // #region same-page
    /// <summary>
    /// The default listing is three segments read one after another, each off its own order, and
    /// gives the page the full sort gives for every filter, offset and instant tried, with the same
    /// total: in memory, and through a store with the listing cache in front of it.
    /// </summary>
    [Theory]
    [InlineData(null, null, 0, 100)]
    [InlineData("Ford", null, 0, 25)]
    [InlineData(null, AuctionStatus.Live, 10, 30)]
    [InlineData(null, AuctionStatus.Upcoming, 0, 500)]
    [InlineData(null, AuctionStatus.Ended, 40, 40)]
    [InlineData("Kia", null, 900, 100)]
    public async Task The_default_listing_is_the_page_the_full_sort_gives(string? make, AuctionStatus? status, int offset, int limit)
    {
        var seeds = Enumerable.Range(0, 1_000).Select(i => TestData.Vehicle(id: $"v-{i}", make: i % 3 == 0 ? "Ford" : "Kia")).ToArray();
        var memory = new InventoryService(new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool));
        var store = new InventoryService(new CountingStore(seeds, seeds.Length, AllAtWarmUp), new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);
        var filter = new VehicleFilter { Make = make, Status = status };

        foreach (int hours in new[] { 0, 7, 30, 75, 140 })
        {
            var now = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero).AddHours(hours));
            var matched = seeds.Where(filter.Compile(now)).ToList();
            var expected = VehicleOrdering.Sort(matched, VehicleSort.EndingSoonest, now).Skip(offset).Take(limit).Select(v => v.Id).ToList();

            foreach (var service in new[] { memory, store })
            {
                var page = await service.SearchAsync(filter, now, VehicleSort.EndingSoonest, limit, offset);
                Assert.Equal(matched.Count, page.Total);
                Assert.Equal(expected, page.Vehicles.Select(v => v.Id));
            }
        }
    }

    [Fact]
    public async Task A_page_is_the_rows_a_full_sort_would_have_given_ties_included()
    {
        // Four prices across forty vehicles, so every price sort is mostly ties and only a stable order passes.
        var seeds = Enumerable.Range(0, 40)
            .Select(i => TestData.Vehicle(id: $"v-{i}", currentBid: 10_000 + (i % 4 * 1_000)))
            .ToArray();
        var service = new InventoryService(new CountingStore(seeds, seeds.Length, AllAtWarmUp), new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);
        (int Offset, int Limit)[] pages = [(0, 10), (10, 10), (35, 10), (0, 100), (40, 5), (0, int.MaxValue)];

        foreach (VehicleSort sort in Enum.GetValues<VehicleSort>())
        {
            List<string> whole = VehicleOrdering.Sort(seeds, sort, Noon).Select(v => v.Id).ToList();
            foreach ((int offset, int limit) in pages)
            {
                var page = await service.SearchAsync(new VehicleFilter(), Noon, sort, limit, offset);

                Assert.Equal(40, page.Total);
                Assert.Equal(whole.Skip(offset).Take(limit).ToList(), page.Vehicles.Select(v => v.Id).ToList());
            }
        }
    }
    // #endregion same-page

    // #region overlay-merge
    /// <summary>
    /// A bid or the room moves a price the store does not hold. Sorted or filtered by price, the
    /// store leaves those vehicles out and the service merges them back from their raised figures,
    /// and every page, at every offset, is the page the full sort over the raised catalogue gives.
    /// </summary>
    [Fact]
    public async Task A_raised_price_sorts_and_filters_where_the_full_sort_puts_it_on_every_page()
    {
        var seeds = Enumerable.Range(0, 300)
            .Select(i => TestData.Vehicle(id: $"v-{i}", currentBid: 5_000 + (i % 37 * 500)))
            .ToArray();
        var raisedTo = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["v-3"] = 90_000,
            ["v-150"] = 4_000,
            ["v-151"] = 12_500,
            ["v-299"] = 12_500,
            ["v-7"] = 30_000,
        };
        Vehicle Raise(Vehicle v) => raisedTo.TryGetValue(v.Id, out int amount) ? v with { CurrentBid = Math.Max(amount, v.CurrentBid ?? 0), BidCount = v.BidCount + 5 } : v;
        var overlay = new ListingOverlay(raisedTo.Keys.ToList(), Raise);
        var raised = seeds.Select(Raise).ToList();
        var store = new CountingStore(seeds, seeds.Length, AllAtWarmUp);
        var service = new InventoryService(store, new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);
        VehicleFilter[] filters = [new(), new() { PriceMin = 12_000 }, new() { PriceMax = 12_500, PriceMin = 4_000 }];
        (int Offset, int Limit)[] pages = [(0, 10), (1, 3), (4, 20), (100, 50), (290, 20), (0, int.MaxValue)];

        foreach (var sort in new[] { VehicleSort.PriceAsc, VehicleSort.PriceDesc, VehicleSort.MostBids, VehicleSort.EndingSoonest })
        {
            foreach (var filter in filters)
            {
                var matched = raised.Where(filter.Compile(Noon)).ToList();
                var whole = VehicleOrdering.Sort(matched, sort, Noon).Select(v => v.Id).ToList();
                foreach ((int offset, int limit) in pages)
                {
                    var page = await service.SearchAsync(filter, Noon, sort, limit, offset, overlay);

                    Assert.Equal(matched.Count, page.Total);
                    Assert.Equal(whole.Skip(offset).Take(limit).ToList(), page.Vehicles.Select(v => v.Id).ToList());
                    Assert.All(page.Vehicles, v => Assert.Equal(Raise(seeds.First(seed => seed.Id == v.Id)).CurrentBid, v.CurrentBid));
                }
            }
        }

        // And never by handing the store the moved ids: a count or a page with the ids in its query
        // reads every row and is cached under a key that changes with every bid (ADR: The store does
        // the work), so the store is only ever asked the same question every visitor asks.
        Assert.Equal(0, store.AskedToExclude);
    }
    // #endregion overlay-merge

    [Fact]
    public async Task Facets_return_sorted_distinct_values()
    {
        var service = new InventoryService(
            new FakeVehicles(
                TestData.Vehicle(id: "a", make: "Kia"),
                TestData.Vehicle(id: "b", make: "Ford"),
                TestData.Vehicle(id: "c", make: "Kia")),
            new FakeManifest(TestData.SuvPool));

        Assert.Equal(["Ford", "Kia"], (await service.FacetsAsync()).Makes);
    }

    /// <summary>
    /// The facets are read once, at warm-up, and every request reads the same instance (ADR: The
    /// search index, addendum). Reference equality is the fact worth holding: a rebuild would hand
    /// back a new object with the same values, and a value comparison would not notice.
    /// </summary>
    [Fact]
    public async Task Facets_are_built_once_with_the_catalogue()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(id: "a", make: "Kia"), TestData.Vehicle(id: "b", make: "Ford")),
            new FakeManifest(TestData.SuvPool));

        Assert.Same(await service.FacetsAsync(), await service.FacetsAsync());
    }

    [Fact]
    public async Task Leaves_non_image_fields_untouched()
    {
        var original = TestData.Vehicle(currentBid: null);
        var service = new InventoryService(new FakeVehicles(original), new FakeManifest(TestData.SuvPool));

        var vehicle = (await service.FindAsync(original.Id))!;
        Assert.Equal(original with { Images = vehicle.Images }, vehicle);
    }

    // #region held-in-memory
    /// <summary>
    /// The rule this lane exists for (ADR: The store does the work, rule a): with the store full,
    /// the process holds no catalogue. The listing, a vehicle and the room's picks all come from
    /// the store, and the count held in memory reads zero, which is the number the Admin tab shows.
    /// </summary>
    [Fact]
    public async Task With_the_store_full_the_process_holds_no_catalogue()
    {
        var seeds = Enumerable.Range(0, 500).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var store = new CountingStore(seeds, seeds.Length, AllAtWarmUp);
        var seeder = new FakeVehicles(seeds);
        var service = new InventoryService(store, seeder, new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Fill);

        await service.WarmAsync();
        var page = await service.SearchAsync(new VehicleFilter(), Noon, VehicleSort.EndingSoonest, 24, 0);
        var live = await service.FirstLiveAsync(Noon, 40);

        Assert.Equal(new CatalogueReport("store", 0, 500, 500, null), service.Report());
        Assert.Equal(0, seeder.LoadCalls);
        Assert.Equal(500, page.Total);
        // And the default listing's first page cost the store a page and no count: unfiltered, the
        // total is the catalogue's size, and a page that starts inside the live segment needs no
        // segment's size to find its rows.
        Assert.True(store.Pages > 0);
        Assert.Equal(0, store.Counts);
        Assert.All(live, v => Assert.Equal(AuctionStatus.Live, AuctionSchedule.StatusFor(v.Id, Noon)));
    }

    /// <summary>
    /// A live page cached earlier in its five seconds is checked against the moment it is served:
    /// an auction that ended inside the bucket is never handed out among the live ones.
    /// </summary>
    [Fact]
    public async Task A_cached_live_page_never_shows_an_auction_that_has_ended_since()
    {
        var seeds = Enumerable.Range(0, 300).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var store = new CountingStore(seeds, seeds.Length, AllAtWarmUp);
        var service = new InventoryService(store, new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);
        var ending = seeds
            .Select(vehicle => (vehicle.Id, Slot: CatalogueSlot.For(vehicle.Id)))
            .First(pick => pick.Slot.EndOffsetMs > 0 && pick.Slot.EndOffsetMs % 5_000 is >= 1_500 and <= 4_500
                && pick.Slot.EndOffsetMs - pick.Slot.StartOffsetMs > 2_000);
        long anchor = Noon.AnchorMs;
        var before = new AuctionClock(anchor + ending.Slot.EndOffsetMs - 1_000, anchor);
        var after = new AuctionClock(anchor + ending.Slot.EndOffsetMs + 10, anchor);
        var live = new VehicleFilter { Status = AuctionStatus.Live };

        var first = await service.SearchAsync(live, before, VehicleSort.EndingSoonest, 500, 0);
        var second = await service.SearchAsync(live, after, VehicleSort.EndingSoonest, 500, 0);

        Assert.Contains(first.Vehicles, vehicle => vehicle.Id == ending.Id);
        Assert.DoesNotContain(second.Vehicles, vehicle => vehicle.Id == ending.Id);
    }

    /// <summary>The listing cache answers the same query in the same five seconds without asking the store again, and stays inside its bounds.</summary>
    [Fact]
    public async Task The_same_listing_inside_five_seconds_is_answered_without_the_store()
    {
        var seeds = Enumerable.Range(0, 200).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var store = new CountingStore(seeds, seeds.Length, AllAtWarmUp);
        var service = new InventoryService(store, new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);

        await service.SearchAsync(new VehicleFilter(), Noon, VehicleSort.PriceAsc, 24, 0);
        int pages = store.Pages, counts = store.Counts;
        await service.SearchAsync(new VehicleFilter(), Noon, VehicleSort.PriceAsc, 24, 0);

        Assert.Equal((pages, counts), (store.Pages, store.Counts));
    }

    /// <summary>
    /// A store missing more than it fills at warm-up is filled beside the site: the expansion answers
    /// from memory meanwhile, the whole of it is written, and then the store answers and the list is
    /// let go.
    /// </summary>
    [Fact]
    public async Task A_store_that_is_short_is_filled_beside_the_site_and_then_answers()
    {
        var seeds = Enumerable.Range(0, 450).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var store = new CountingStore(seeds, 120, new CatalogueFillPace(100, TimeSpan.Zero, 10));
        var service = new InventoryService(store, new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Fill);

        await service.WarmAsync();
        Assert.NotNull(service.Filling);
        await service.Filling!;

        var report = service.Report();
        Assert.Equal(("store", 0, 450), (report.Mode, report.HeldInMemory, report.Filled));
        Assert.True(store.Written >= 450 - 120);
        Assert.Equal(450, (await service.SearchAsync(new VehicleFilter(), Noon)).Total);
    }

    /// <summary>A short store and upkeep off, which is every test host: the list answers, and the report says so and how many it holds.</summary>
    [Fact]
    public async Task A_short_store_with_upkeep_off_answers_from_memory_and_says_so()
    {
        var seeds = Enumerable.Range(0, 300).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var store = new CountingStore(seeds, 0, new CatalogueFillPace(100, TimeSpan.Zero, 10));
        var service = new InventoryService(store, new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool), CatalogueUpkeep.Off);

        await service.WarmAsync();

        var report = service.Report();
        Assert.Equal(("memory", 300), (report.Mode, report.HeldInMemory));
        Assert.NotNull(report.Note);
        Assert.Equal(0, store.Written);
        Assert.Null(service.Filling);
    }
    // #endregion held-in-memory
}
