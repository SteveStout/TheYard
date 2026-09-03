using System.Diagnostics;
using TheBlock.Application;
using TheBlock.Data;
using TheBlock.Domain;
using TheBlock.Infrastructure;
using Xunit.Abstractions;

namespace TheBlock.Tests;

file sealed class BenchSeed(params Vehicle[] vehicles) : IVehicleSource
{
    public IReadOnlyList<Vehicle> Load() => vehicles;
}

/// <summary>
/// What the index actually buys, measured rather than asserted (ADR: Search
/// index). This compares the two paths directly over the same hundred thousand
/// rows the site serves, with no HTTP, no sorting and no serialisation in the
/// way, because a request's wall-clock time is mostly those three and they
/// drown the difference.
///
/// The assertion is deliberately loose. A tight timing assertion on a shared
/// build agent is a test that fails for reasons unrelated to the code, and a
/// suite people learn to re-run is worse than no suite. This one catches a
/// regression (the indexed path becoming slower) and reports the real numbers
/// through test output for anyone who wants them.
/// </summary>
public class SearchIndexBenchmarkTests(ITestOutputHelper output)
{
    private const int Rows = 100_000;

    private static readonly AuctionClock Now =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    [Fact]
    public void The_index_is_not_slower_than_rebuilding_the_text_per_row()
    {
        var vehicles = new SyntheticVehicleSource(
            new BenchSeed(
                TestData.Vehicle(id: "seed-1", make: "Ford", bodyStyle: "SUV"),
                TestData.Vehicle(id: "seed-2", make: "Kia", bodyStyle: "sedan"),
                TestData.Vehicle(id: "seed-3", make: "Toyota", bodyStyle: "truck")),
            Rows).Load();
        var index = new VehicleSearchIndex(vehicles);
        var filter = new VehicleFilter { Query = "ford" };

        // Both paths once first: the JIT compiles on first call, and timing
        // that instead of the work is the classic way to measure nothing.
        Run(filter.Compile(Now), vehicles);
        Run(filter.Compile(Now, index), vehicles);

        long without = Median(filter.Compile(Now), vehicles);
        long with = Median(filter.Compile(Now, index), vehicles);

        output.WriteLine($"{Rows:N0} rows, query \"ford\", median of 5 scans:");
        output.WriteLine($"  text rebuilt per row: {without} ms");
        output.WriteLine($"  index:                {with} ms");
        output.WriteLine($"  difference:           {without - with} ms");

        // The bound that matters is "did not regress". The real number is in
        // the output above and in the record.
        Assert.True(
            with <= without,
            $"the indexed scan took {with} ms against {without} ms without the index");
    }

    [Fact]
    public void The_index_covers_every_row_the_dataset_loads()
    {
        var vehicles = new SyntheticVehicleSource(
            new BenchSeed(TestData.Vehicle(id: "seed-1")), 5_000).Load();

        var index = new VehicleSearchIndex(vehicles);

        Assert.Equal(vehicles.Count, index.Count);
        // A miss would be silent: the fallback would compute the text and the
        // answer would still be right, just slower. So assert the coverage.
        Assert.All(vehicles, vehicle =>
            Assert.Equal(VehicleSearchIndex.TextFor(vehicle), index.For(vehicle)));
    }

    private static long Median(Func<Vehicle, bool> predicate, IReadOnlyList<Vehicle> vehicles)
    {
        var runs = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            runs.Add(Run(predicate, vehicles));
        }
        runs.Sort();
        return runs[runs.Count / 2];
    }

    private static long Run(Func<Vehicle, bool> predicate, IReadOnlyList<Vehicle> vehicles)
    {
        var clock = Stopwatch.StartNew();
        int matched = 0;
        foreach (var vehicle in vehicles)
        {
            if (predicate(vehicle))
            {
                matched++;
            }
        }
        clock.Stop();
        // Used, so the loop cannot be optimised away.
        Assert.True(matched >= 0);
        return clock.ElapsedMilliseconds;
    }
}
