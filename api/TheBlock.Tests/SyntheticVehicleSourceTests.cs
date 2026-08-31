using TheBlock.Application;
using TheBlock.Domain;
using TheBlock.Infrastructure;
using TheBlock.Data;

namespace TheBlock.Tests;

file sealed class SeedSource(params Vehicle[] vehicles) : IVehicleSource
{
    public IReadOnlyList<Vehicle> Load() => vehicles;
}

public class SyntheticVehicleSourceTests
{
    private static readonly Vehicle[] Seeds =
    [
        TestData.Vehicle(id: "seed-1", make: "Ford"),
        TestData.Vehicle(id: "seed-2", make: "Kia", bodyStyle: "sedan"),
    ];

    [Fact]
    public void Expands_to_the_target_count_with_unique_ids()
    {
        var vehicles = new SyntheticVehicleSource(new SeedSource(Seeds), 1_000).Load();

        Assert.Equal(1_000, vehicles.Count);
        Assert.Equal(1_000, vehicles.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public void Is_deterministic_across_loads()
    {
        var first = new SyntheticVehicleSource(new SeedSource(Seeds), 500).Load();
        var second = new SyntheticVehicleSource(new SeedSource(Seeds), 500).Load();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Passes_the_seeds_through_untouched_when_the_target_is_not_larger()
    {
        var vehicles = new SyntheticVehicleSource(new SeedSource(Seeds), 2).Load();
        Assert.Equal(Seeds, vehicles);
    }

    [Fact]
    public void Variants_keep_the_dataset_invariants()
    {
        var vehicles = new SyntheticVehicleSource(new SeedSource(Seeds), 2_000).Load();

        Assert.All(vehicles, v =>
        {
            Assert.InRange(v.ConditionGrade, 1.0, 5.0);
            Assert.InRange(v.Year, 2016, 2026);
            Assert.True(v.CurrentBid is null == (v.BidCount == 0), $"{v.Id}: bid/bid_count mismatch");
            if (v.CurrentBid is { } bid)
            {
                Assert.True(bid >= v.StartingBid, $"{v.Id}: bid below opening ask");
            }
        });
        // The mix matters for the UI: some of everything.
        Assert.Contains(vehicles, v => v.CurrentBid is null);
        Assert.Contains(vehicles, v => v.CurrentBid is not null);
        Assert.Contains(vehicles, v => v.ReservePrice is null);
        Assert.Contains(vehicles, v => v.BuyNowPrice is not null);
    }

    [Fact]
    public void Variants_inherit_the_seed_identity_fields()
    {
        var vehicles = new SyntheticVehicleSource(new SeedSource(Seeds), 100).Load();

        Assert.All(vehicles, v => Assert.Contains(v.Make, new[] { "Ford", "Kia" }));
    }
}
