using TheYard.Application;
using TheYard.Data;
using TheYard.Infrastructure;

namespace TheYard.Tests;

file sealed class SeedSource(params Vehicle[] vehicles) : IVehicleSource
{
    public Task<IReadOnlyList<Vehicle>> LoadAsync() => Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
}

public class SyntheticVehicleSourceTests
{
    private static readonly Vehicle[] Seeds =
    [
        TestData.Vehicle(id: "seed-1", make: "Ford"),
        TestData.Vehicle(id: "seed-2", make: "Kia", bodyStyle: "sedan"),
    ];

    [Fact]
    public async Task Expands_to_the_target_count_with_unique_ids()
    {
        var vehicles = await new SyntheticVehicleSource(new SeedSource(Seeds), 1_000).LoadAsync();

        Assert.Equal(1_000, vehicles.Count);
        Assert.Equal(1_000, vehicles.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public async Task Is_deterministic_across_loads()
    {
        var first = await new SyntheticVehicleSource(new SeedSource(Seeds), 500).LoadAsync();
        var second = await new SyntheticVehicleSource(new SeedSource(Seeds), 500).LoadAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Passes_the_seeds_through_untouched_when_the_target_is_not_larger()
    {
        var vehicles = await new SyntheticVehicleSource(new SeedSource(Seeds), 2).LoadAsync();
        Assert.Equal(Seeds, vehicles);
    }

    /// <summary>
    /// A variant's grade stays inside its seed's band, because everything else
    /// that describes the vehicle's condition comes from the seed unchanged.
    ///
    /// <para>It did not, and the result was visible on the live site: "2.0
    /// Rough" printed above "Vehicle shows average wear for its age and
    /// mileage. Mechanically sound." and "No damage reported." The grade was
    /// drawn from the hash across the whole scale while the report, the damage
    /// notes and the title came from the seed, so three fields on one page
    /// disagreed about the same car.</para>
    /// </summary>
    [Theory]
    [InlineData(1.4)]
    [InlineData(2.0)]
    [InlineData(3.7)]
    [InlineData(4.2)]
    [InlineData(5.0)]
    public async Task A_variants_grade_stays_in_its_seeds_band(double seedGrade)
    {
        var seed = TestData.Vehicle(id: "seed-graded") with { ConditionGrade = seedGrade };
        double band = Math.Floor(seedGrade);

        var variants = (await new SyntheticVehicleSource(new SeedSource(seed), 400).LoadAsync()).Skip(1);

        Assert.All(variants, vehicle =>
            Assert.InRange(vehicle.ConditionGrade, band, Math.Min(5.0, band + 0.9)));
    }

    [Fact]
    public async Task Variants_keep_the_dataset_invariants()
    {
        var vehicles = await new SyntheticVehicleSource(new SeedSource(Seeds), 2_000).LoadAsync();

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
    public async Task Variants_inherit_the_seed_identity_fields()
    {
        var vehicles = await new SyntheticVehicleSource(new SeedSource(Seeds), 100).LoadAsync();

        Assert.All(vehicles, v => Assert.Contains(v.Make, new[] { "Ford", "Kia" }));
    }
}
