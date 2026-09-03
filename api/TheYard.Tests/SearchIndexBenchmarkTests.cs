using System.Diagnostics;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;
using TheYard.Infrastructure;
using Xunit.Abstractions;

namespace TheYard.Tests;

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
/// The assertion is deliberately loose: best of five runs on each path, and a
/// quarter of headroom on top. A tight timing assertion on a shared build agent
/// is a test that fails for reasons unrelated to the code, and a suite people
/// learn to re-run is worse than no suite. This one catches a regression (the
/// indexed path becoming meaningfully slower) and reports the real numbers
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

        // Best of five each, and the two paths take turns.
        //
        // Best of five because both paths do identical work on identical data;
        // what varies between runs is how often the operating system took the
        // core away, and that only ever adds time, so the fastest run is the
        // closest either path gets to the work itself.
        //
        // Turns because the first version measured one path five times and then
        // the other five times, which compares two numbers taken at two
        // different moments on a shared machine. It failed at 243 ms against
        // 115 ms with eight dotnet processes on eight cores, and passed six
        // times out of six alone, five minutes later, unchanged. Alternating
        // means a busy stretch lands on both paths rather than on whichever one
        // happened to be running through it. The threshold was not touched: a
        // measurement that is unfair is not fixed by widening what it allows
        // (ADR: The exemption that hid a contrast failure).

        var (without, with) = FastestByTurns(filter.Compile(Now), filter.Compile(Now, index), vehicles);

        output.WriteLine($"{Rows:N0} rows, query \"ford\", best of 5 alternating scans:");
        output.WriteLine($"  text rebuilt per row: {without} ms");
        output.WriteLine($"  index:                {with} ms");
        output.WriteLine($"  difference:           {without - with} ms");

        // The bound that matters is "did not regress", and a regression worth
        // a red build is not one millisecond. The comment above this class has
        // always said the assertion is loose; until five suites ran back to
        // back and this failed at 95 ms against 73 ms, it was not.
        long allowed = without + Math.Max(5, without / 4);
        Assert.True(
            with <= allowed,
            $"the indexed scan took {with} ms against {without} ms without the "
                + $"index, past the {allowed} ms this allows for a busy machine");
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

    /// <summary>
    /// Five rounds, both paths in each, so the machine's mood is shared out
    /// between them rather than landing on one.
    /// </summary>
    private static (long Without, long With) FastestByTurns(
        Func<Vehicle, bool> plain, Func<Vehicle, bool> indexed, IReadOnlyList<Vehicle> vehicles)
    {
        long bestPlain = long.MaxValue;
        long bestIndexed = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            bestPlain = Math.Min(bestPlain, Run(plain, vehicles));
            bestIndexed = Math.Min(bestIndexed, Run(indexed, vehicles));
        }
        return (bestPlain, bestIndexed);
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
