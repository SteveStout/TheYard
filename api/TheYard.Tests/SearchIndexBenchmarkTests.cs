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
/// What the index actually buys, measured rather than asserted (ADR: The
/// search index). This compares the two paths directly over the same hundred
/// thousand rows the site serves, with no HTTP, no sorting and no serialisation
/// in the way, because a request's wall-clock time is mostly those three and
/// they drown the difference.
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

        // Paired rounds, and the comparison is of the pairs.
        //
        // Three versions of this, and the first two were both measuring the
        // machine as much as the code. The first timed one path five times and
        // then the other five times, which compares two numbers taken at two
        // different moments on a shared machine: it failed at 243 ms against
        // 115 ms with eight dotnet processes running, and passed six times of
        // six alone five minutes later, unchanged. The second alternated them
        // and took each path's fastest, which is better and still lets one
        // path's best come from a quiet moment and the other's from a busy one.
        //
        // This one measures both paths inside each round and compares them
        // there, then takes the median of the five differences. A round that
        // lands in a busy stretch has both of its numbers inflated, so the
        // difference survives it, and the median throws away the worst round
        // rather than being dragged by it. That is a paired comparison, which
        // is the standard answer to "two measurements, one noisy environment".
        //
        // The threshold has never been widened through any of this. A
        // measurement that is unfair is not fixed by allowing more
        // (ADR: The exemption that hid a contrast failure).
        var rounds = PairedRounds(filter.Compile(Now), filter.Compile(Now, index), vehicles);
        long[] differences = rounds.Select(round => round.With - round.Without).Order().ToArray();
        long typical = differences[differences.Length / 2];
        long without = rounds.Select(round => round.Without).Order().ToArray()[rounds.Length / 2];

        output.WriteLine($"{Rows:N0} rows, query \"ford\", five paired rounds:");
        foreach (var (plain, indexed) in rounds)
        {
            output.WriteLine($"  without {plain,4} ms   index {indexed,4} ms   difference {indexed - plain,5} ms");
        }
        output.WriteLine($"  median difference: {typical} ms (negative means the index is faster)");

        // The bound that matters is "did not regress", and a regression worth a
        // red build is not one millisecond. A quarter of the unindexed time, or
        // five milliseconds, whichever is larger.
        long allowed = Math.Max(5, without / 4);
        Assert.True(
            typical <= allowed,
            $"the index was typically {typical} ms slower per scan, past the {allowed} ms "
                + $"this allows against an unindexed scan of about {without} ms");
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
    /// Five rounds, both paths measured inside each one, so a round that lands
    /// in a busy stretch inflates both of its numbers and the difference
    /// between them survives it.
    /// </summary>
    private static (long Without, long With)[] PairedRounds(
        Func<Vehicle, bool> plain, Func<Vehicle, bool> indexed, IReadOnlyList<Vehicle> vehicles)
    {
        var rounds = new (long Without, long With)[5];
        for (int round = 0; round < rounds.Length; round++)
        {
            rounds[round] = (Run(plain, vehicles), Run(indexed, vehicles));
        }
        return rounds;
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
