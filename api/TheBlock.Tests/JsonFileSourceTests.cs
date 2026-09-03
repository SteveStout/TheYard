using TheBlock.Infrastructure;

namespace TheBlock.Tests;

public class JsonFileSourceTests
{
    [Fact]
    public void Reads_snake_case_json_into_typed_vehicles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vehicles-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            [{
              "id": "abc", "vin": "V1", "year": 2023, "make": "Ford", "model": "Bronco",
              "trim": "Big Bend", "body_style": "SUV", "exterior_color": "Burgundy",
              "interior_color": "Beige", "engine": "V6", "transmission": "automatic",
              "drivetrain": "4WD", "odometer_km": 47731, "fuel_type": "gasoline",
              "condition_grade": 3.8, "condition_report": "ok", "damage_notes": ["a"],
              "title_status": "clean", "province": "Ontario", "city": "Toronto",
              "auction_start": "2026-04-05T14:00:00", "starting_bid": 14500,
              "reserve_price": null, "buy_now_price": 28000,
              "images": ["x.jpg"], "selling_dealership": "Dealer", "lot": "A-1",
              "current_bid": null, "bid_count": 0
            }]
            """);
        try
        {
            var vehicle = Assert.Single(new JsonFileVehicleSource(path).Load());

            Assert.Equal("abc", vehicle.Id);
            Assert.Equal("SUV", vehicle.BodyStyle);
            Assert.Equal(47731, vehicle.OdometerKm);
            Assert.Equal(3.8, vehicle.ConditionGrade);
            Assert.Null(vehicle.ReservePrice);
            Assert.Equal(28000, vehicle.BuyNowPrice);
            Assert.Null(vehicle.CurrentBid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loads_the_real_repository_dataset()
    {
        var vehicles = new JsonFileVehicleSource(Path.Combine(RepoRoot(), "data", "vehicles.json")).Load();

        Assert.Equal(200, vehicles.Count);
        Assert.Equal(200, vehicles.Select(v => v.Id).Distinct().Count());
        // The dataset ships vehicles with and without bids; both shapes must parse.
        Assert.Contains(vehicles, v => v.CurrentBid is null && v.BidCount == 0);
        Assert.Contains(vehicles, v => v.CurrentBid is not null);
    }

    [Fact]
    public void Loads_the_real_photo_manifest()
    {
        var manifest = new JsonFilePhotoManifestSource(
            Path.Combine(RepoRoot(), "api", "TheBlock.Api", "photo-manifest.json")).Load();

        Assert.Equal(50, manifest.Count);
        Assert.Equal(5, manifest.Select(photo => photo.Style).Distinct().Count());
    }

    /// <summary>Walks up from the test assembly to the repository root.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "vehicles.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
