using TheBlock.Application;
using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Tests;

file sealed class FakeVehicles(params Vehicle[] vehicles) : IVehicleSource
{
    public int LoadCalls { get; private set; }

    public IReadOnlyList<Vehicle> Load()
    {
        LoadCalls++;
        return vehicles;
    }
}

file sealed class FakeManifest(params PhotoEntry[] photos) : IPhotoManifestSource
{
    public IReadOnlyList<PhotoEntry> Load() => photos;
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

    [Fact]
    public void Leaves_non_image_fields_untouched()
    {
        var original = TestData.Vehicle(currentBid: null);
        var service = new InventoryService(new FakeVehicles(original), new FakeManifest(TestData.SuvPool));

        var vehicle = Assert.Single(service.GetAll());
        Assert.Equal(original with { Images = vehicle.Images }, vehicle);
    }
}
