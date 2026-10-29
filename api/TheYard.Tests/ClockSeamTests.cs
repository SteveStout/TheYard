using Microsoft.Extensions.Logging;

namespace TheYard.Tests;

/// <summary>
/// The clock is handed in, and the thing it was handed to uses it (ADR: Onion and SOLID, how
/// this codebase holds them, the addendum on the clock).
///
/// <para>Every case here moves a clock the test owns and reads the moment that came out the
/// other end. That is deliberate and it is the whole point: a class that takes a
/// <see cref="TimeProvider"/> and then reads <c>DateTimeOffset.UtcNow</c> anyway satisfies the
/// count in the ratchet file and nothing else, and a test that only asserted the parameter
/// exists would pass on exactly that code. So none of these assert on a signature. They each
/// set a clock to a moment nobody's real clock will ever be at, and insist the stamp is that
/// moment.</para>
/// </summary>
public class ClockSeamTests
{
    /// <summary>A clock stopped at one moment, which the test moves by hand.</summary>
    private sealed class Stopped(DateTimeOffset now) : TimeProvider
    {
        /// <summary>The moment this clock reads, which a test may set again.</summary>
        public DateTimeOffset Now { get; set; } = now;

        /// <summary>The moment, UTC.</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A moment no real clock will be at while this suite runs.</summary>
    private static readonly DateTimeOffset Then = new(1999, 12, 31, 23, 59, 30, TimeSpan.Zero);

    [Fact]
    public void An_error_the_server_records_is_stamped_by_the_clock_the_rings_were_handed()
    {
        var clock = new Stopped(Then);
        var rings = new ErrorRings(clock);

        rings.Server.Record("/api/vehicles", 500, "InvalidOperationException");
        clock.Now = Then.AddMinutes(5);
        rings.Browser.Record("/listing", 0, "TypeError");

        Assert.Equal(Then, Assert.Single(rings.Server.Snapshot()).At);
        Assert.Equal(Then.AddMinutes(5), Assert.Single(rings.Browser.Snapshot()).At);
    }

    [Fact]
    public void The_moment_this_process_started_is_the_clock_it_was_handed_before_and_after_the_mark()
    {
        var clock = new Stopped(Then);
        var start = new HostStart(clock);

        Assert.Equal(Then, start.At);

        clock.Now = Then.AddSeconds(90);
        start.Mark();
        Assert.Equal(Then.AddSeconds(90), start.At);
    }

    [Fact]
    public void A_log_line_in_the_ring_carries_the_clock_the_provider_was_handed()
    {
        var clock = new Stopped(Then);
        var ring = new LogRingBuffer(10);
        using var provider = new RingBufferLoggerProvider(ring, clock);

        provider.CreateLogger("TheYard.Api.Test").LogWarning("a line the ring keeps");

        var line = Assert.Single(ring.Snapshot());
        Assert.Equal(Then, line.At);
    }

    [Fact]
    public async Task A_log_line_offered_to_the_collector_carries_the_clock_the_provider_was_handed()
    {
        var clock = new Stopped(Then);
        var store = new OneBatchStore();
        var collector = new LogCollector(store);
        using var provider = new CollectorLoggerProvider(collector, clock, () => ("sql", "/api/vehicles", "trace-1"));

        provider.CreateLogger("TheYard.Something").LogWarning("a line the collector takes");
        await collector.DrainAsync(CancellationToken.None);

        Assert.Equal(Then, Assert.Single(store.Written).At);
    }

    /// <summary>A log store that keeps what it is handed, so the test can read the stamp back.</summary>
    private sealed class OneBatchStore : ILogStore
    {
        public readonly List<LogEvent> Written = [];

        public Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new LogAvailability(true, "a list"));

        public Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation)
        {
            Written.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogEvent>>(Written);

        public Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogCount>>([]);

        public Task<KeptRingPage> RingAsync(string kind, string site, DateTimeOffset since, int take, CancellationToken cancellation) =>
            Task.FromResult(new KeptRingPage([], 0));
    }
}
