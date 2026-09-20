using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

// #region fakes
// Fakes at the ports. The `file` modifier keeps these two classes private to
// this file; each stands in for a JSON adapter, so the service is tested with
// no filesystem, and LoadCalls proves the dataset is read exactly once.
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

public class InventoryServiceTests
{
    [Fact]
    public void Rewrites_images_to_the_configured_prefix()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(bodyStyle: "SUV")),
            new FakeManifest(TestData.SuvPool),
            "/api/images");

        var vehicle = Assert.Single(service.GetAll());
        Assert.Equal(PhotoGallery.GallerySize, vehicle.Images.Count);
        Assert.All(vehicle.Images, url => Assert.StartsWith("/api/images/suv-", url));
    }
    // #endregion fakes

    /// <summary>
    /// A load that failed is tried again by the next caller (ADR: The ports
    /// learn to wait, addendum). The Lazy this replaced kept the first failure
    /// for the life of the process, which is a container that stays broken
    /// after a one-second outage at startup while its log says the first
    /// visitor will try again.
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
        Assert.Equal("v-1", Assert.Single(service.GetAll()).Id);
        Assert.Equal(2, source.LoadCalls);
    }

    [Fact]
    public void Keeps_dataset_images_when_the_body_style_has_no_pool()
    {
        var original = TestData.Vehicle(bodyStyle: "van", images: ["https://placehold.co/keep-me"]);
        var service = new InventoryService(new FakeVehicles(original), new FakeManifest(TestData.SuvPool));

        Assert.Equal(["https://placehold.co/keep-me"], Assert.Single(service.GetAll()).Images);
    }

    [Fact]
    public void Finds_vehicles_by_id_and_returns_null_for_unknown_ids()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(id: "v-1"), TestData.Vehicle(id: "v-2")),
            new FakeManifest(TestData.SuvPool));

        Assert.Equal("v-2", service.GetById("v-2")?.Id);
        Assert.Null(service.GetById("nope"));
    }

    [Fact]
    public void Loads_the_sources_once_across_calls()
    {
        var vehicles = new FakeVehicles(TestData.Vehicle());
        var service = new InventoryService(vehicles, new FakeManifest(TestData.SuvPool));

        service.GetAll();
        service.GetById("test-id");
        service.GetAll();

        Assert.Equal(1, vehicles.LoadCalls);
    }

    [Fact]
    public void Search_applies_the_filter_and_an_empty_filter_returns_everything()
    {
        var now = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var service = new InventoryService(
            new FakeVehicles(
                TestData.Vehicle(id: "ford", make: "Ford"),
                TestData.Vehicle(id: "kia", make: "Kia")),
            new FakeManifest(TestData.SuvPool));

        var fords = service.Search(new VehicleFilter { Make = "Ford" }, now);
        Assert.Equal(1, fords.Total);
        Assert.Equal(["ford"], fords.Vehicles.Select(v => v.Id));
        Assert.Equal(2, service.Search(new VehicleFilter(), now).Total);
    }

    [Fact]
    public void Search_pages_but_still_reports_the_full_total()
    {
        var now = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var seeds = Enumerable.Range(0, 25).Select(i => TestData.Vehicle(id: $"v-{i}")).ToArray();
        var service = new InventoryService(new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool));

        var page = service.Search(new VehicleFilter(), now, VehicleSort.EndingSoonest, limit: 10);

        Assert.Equal(25, page.Total);
        Assert.Equal(10, page.Vehicles.Count);
    }

    [Fact]
    public void A_page_is_the_rows_a_full_sort_would_have_given_ties_included()
    {
        var now = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        // Four prices across forty vehicles, so every price sort is mostly ties and only a stable order passes.
        var seeds = Enumerable.Range(0, 40)
            .Select(i => TestData.Vehicle(id: $"v-{i}", currentBid: 10_000 + (i % 4 * 1_000)))
            .ToArray();
        var service = new InventoryService(new FakeVehicles(seeds), new FakeManifest(TestData.SuvPool));
        (int Offset, int Limit)[] pages = [(0, 10), (10, 10), (35, 10), (0, 100), (40, 5), (0, int.MaxValue)];

        foreach (VehicleSort sort in Enum.GetValues<VehicleSort>())
        {
            List<string> whole = VehicleOrdering.Sort(service.GetAll(), sort, now).Select(v => v.Id).ToList();
            foreach ((int offset, int limit) in pages)
            {
                var page = service.Search(new VehicleFilter(), now, sort, limit, offset);
                List<string> expected = whole.Skip(offset).Take(limit).ToList();
                List<string> got = page.Vehicles.Select(v => v.Id).ToList();

                Assert.Equal(40, page.Total);
                Assert.Equal(expected, got);
            }
        }
    }

    [Fact]
    public void Facets_return_sorted_distinct_values()
    {
        var service = new InventoryService(
            new FakeVehicles(
                TestData.Vehicle(id: "a", make: "Kia"),
                TestData.Vehicle(id: "b", make: "Ford"),
                TestData.Vehicle(id: "c", make: "Kia")),
            new FakeManifest(TestData.SuvPool));

        Assert.Equal(["Ford", "Kia"], service.Facets().Makes);
    }

    /// <summary>
    /// The facets are built once, with the catalogue, and every request reads
    /// the same instance. Until 1.0.0.140 each call walked the whole catalogue
    /// four times for an answer that cannot change after load, 12 ms per request
    /// on the live container (ADR: The search index, addendum). Reference
    /// equality is the fact worth holding: a rebuild would hand back a new
    /// object with the same values, and a value comparison would not notice.
    /// </summary>
    [Fact]
    public void Facets_are_built_once_with_the_catalogue()
    {
        var service = new InventoryService(
            new FakeVehicles(TestData.Vehicle(id: "a", make: "Kia"), TestData.Vehicle(id: "b", make: "Ford")),
            new FakeManifest(TestData.SuvPool));

        Assert.Same(service.Facets(), service.Facets());
    }

    [Fact]
    public void Leaves_non_image_fields_untouched()
    {
        var original = TestData.Vehicle(currentBid: null);
        var service = new InventoryService(new FakeVehicles(original), new FakeManifest(TestData.SuvPool));

        var vehicle = Assert.Single(service.GetAll());
        Assert.Equal(original with { Images = vehicle.Images }, vehicle);
    }
}
