namespace TheYard.Tests;

/// <summary>
/// The first screen's day of machine buckets (ADR: One frame for every resource, the addendum on the Cosmos DB card's
/// throttling). Read live on 26 October, the grouped query behind it cost about 450 request units a read and ran up to
/// every fifteen seconds while a first screen was open, the largest single draw on the shared throughput the site's
/// visitors read through. These hold that the day is read whole once, then from a short tail on; that the day it
/// answers is the day a whole read would give; and that it is read whole again after the interval.
/// </summary>
public sealed class PulseDayTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A kept day of five-minute buckets that answers a query the way the store does, and counts what it was asked.</summary>
    private sealed class KeptDay : IMachineHistory
    {
        public Dictionary<DateTimeOffset, int> Requests { get; } = [];

        public List<DateTimeOffset> Asked { get; } = [];

        public Task<MachineHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new MachineHistoryAvailability(true, "kept by the test"));

        public Task AppendAsync(MachineMinute minute, CancellationToken cancellation) => Task.CompletedTask;

        public Task<IReadOnlyList<MachineBucket>> QueryAsync(string site, DateTimeOffset since, MachineGrain grain, CancellationToken cancellation)
        {
            Asked.Add(since);
            var from = MachineWindows.BucketOf(since, grain);
            return Task.FromResult<IReadOnlyList<MachineBucket>>([.. Requests.Where(pair => pair.Key >= from).OrderBy(pair => pair.Key)
                .Select(pair => new MachineBucket(pair.Key, 5, 1024, 400, 420, 100, null, null, null, null, null, 0, 0, pair.Value, 20, 80))]);
        }

        /// <summary>Every five minutes of a day and a half up to <paramref name="end"/>, each with its own count.</summary>
        public void Fill(DateTimeOffset end)
        {
            for (var at = end.AddHours(-36); at <= end; at = at.AddMinutes(5))
            {
                Requests[at] = at.Minute + at.Hour;
            }
        }
    }

    [Fact]
    public async Task The_day_is_read_whole_once_then_from_its_tail_and_answers_what_a_whole_read_would()
    {
        var kept = new KeptDay();
        kept.Fill(Start);
        var day = new PulseDay(kept, "theyard");

        var first = await day.ReadAsync(Start, CancellationToken.None);
        Assert.Equal(Start - PulseReader.Day, kept.Asked[0]);

        // Fifteen seconds on, the newest bucket has filled further and a new one has begun.
        kept.Requests[Start] = 999;
        kept.Requests[Start.AddMinutes(5)] = 7;
        var now = Start.AddMinutes(5).AddSeconds(15);
        var second = await day.ReadAsync(now, CancellationToken.None);

        Assert.Equal(MachineWindows.BucketOf(now - PulseDay.Tail, MachineGrain.FiveMinutes), kept.Asked[1]);
        var whole = await new KeptDayReader(kept).WholeAsync(now);
        Assert.Equal(whole.Select(b => (b.At, b.Requests)), second.Select(b => (b.At, b.Requests)));
        Assert.Equal(999, second.Single(b => b.At == Start).Requests);
        Assert.DoesNotContain(second, b => b.At < MachineWindows.BucketOf(now - PulseReader.Day, MachineGrain.FiveMinutes));
        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public async Task The_day_is_read_whole_again_after_the_interval()
    {
        var kept = new KeptDay();
        kept.Fill(Start.AddHours(2));
        var day = new PulseDay(kept, "theyard");

        await day.ReadAsync(Start, CancellationToken.None);
        await day.ReadAsync(Start.AddMinutes(1), CancellationToken.None);
        await day.ReadAsync(Start + PulseDay.WholeEvery, CancellationToken.None);

        Assert.Equal(3, kept.Asked.Count);
        Assert.Equal(Start - PulseReader.Day, kept.Asked[0]);
        Assert.True(kept.Asked[1] > Start.AddMinutes(1) - PulseDay.Tail.Add(TimeSpan.FromMinutes(5)));
        Assert.Equal(Start + PulseDay.WholeEvery - PulseReader.Day, kept.Asked[2]);
    }

    [Fact]
    public void The_first_screen_reads_its_day_through_the_tail()
    {
        string pulse = File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "Shared", "Instance", "Pulse.cs"));
        Assert.Contains("_day.ReadAsync(now, cancellation)", pulse, StringComparison.Ordinal);
        Assert.DoesNotContain("history.QueryAsync(", pulse, StringComparison.Ordinal);
    }

    /// <summary>What a whole read of the kept day gives at an instant, to hold the folded day against.</summary>
    private sealed class KeptDayReader(KeptDay kept)
    {
        public Task<IReadOnlyList<MachineBucket>> WholeAsync(DateTimeOffset now) =>
            kept.QueryAsync("theyard", now - PulseReader.Day, MachineGrain.FiveMinutes, CancellationToken.None);
    }
}
