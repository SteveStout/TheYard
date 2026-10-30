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

    // #region kept-for-a-window
    /// <summary>
    /// The three classes rung 3 split so they could take a clock each keep an answer for a window,
    /// and a window is a question about now. Each case below reads twice inside the window and once
    /// outside it, on a clock the test moves, and counts what actually went out to the service: a
    /// class that took the clock and read the real one anyway would ask once here and twice there
    /// however far the handed clock was moved.
    ///
    /// <para>The plan's layout is kept for an hour, because apps are added to a plan about never
    /// and the minute read asks for the layout every minute.</para>
    /// </summary>
    [Fact]
    public async Task The_plan_s_layout_is_read_again_only_once_the_handed_clock_has_passed_an_hour()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var clock = new Stopped(Then);
        var layout = new PlanLayout(RecordedCloud.Site, clock);

        var first = await layout.OfAsync("token-1", CancellationToken.None);
        clock.Now = Then.Add(PlanLayout.GoodFor).AddMinutes(-1);
        await layout.OfAsync("token-1", CancellationToken.None);
        int inside = cloud.Asked.Count(address => address.Contains("/sites?api-version", StringComparison.Ordinal));

        clock.Now = Then.Add(PlanLayout.GoodFor).AddMinutes(1);
        await layout.OfAsync("token-1", CancellationToken.None);

        Assert.Equal(RecordedCloud.Plan, first.PlanId);
        Assert.Equal("asp-theyard-ss", layout.PlanName);
        Assert.Equal(1, inside);
        Assert.Equal(2, cloud.Asked.Count(address => address.Contains("/sites?api-version", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The telemetry reading is kept for a minute, failures included, because the Admin tab is a
    /// page someone leaves open and the query costs a token exchange plus a round trip. The
    /// recorded cloud has no telemetry component, so every ask comes back as the note the card
    /// renders, which is kept exactly as an answer would be.
    /// </summary>
    [Fact]
    public async Task The_telemetry_reading_is_asked_for_again_only_once_the_handed_clock_has_passed_a_minute()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var clock = new Stopped(Then);
        var reader = new TelemetryReader(new TelemetrySource("app-1", "client-1"), enabled: true, clock);

        var first = await reader.GetRecentAsync(CancellationToken.None);
        clock.Now = Then.Add(TelemetryReader.GoodFor).AddSeconds(-1);
        var inside = await reader.GetRecentAsync(CancellationToken.None);

        clock.Now = Then.Add(TelemetryReader.GoodFor).AddSeconds(1);
        var outside = await reader.GetRecentAsync(CancellationToken.None);

        // The same answer object, not an equal one: the keep hands back what it holds.
        Assert.Same(first, inside);
        Assert.NotSame(first, outside);
        // The component's own query, not the token exchange that comes before it: the token is asked
        // for at the metadata endpoint with this same service named as the scope.
        Assert.Equal(2, cloud.Asked.Count(address => address.Contains("/v1/apps/app-1/query", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The proof will not start a run within a minute of the last one finishing, because starting
    /// one is a write and the accounts it bids with are made once per process (ADR: The one write a
    /// stranger can make, addendum). Both ends of that minute are the handed clock: the moment the
    /// last run finished is stamped by it, and the refusal is measured from that stamp.
    /// </summary>
    [Fact]
    public async Task A_proof_run_is_refused_inside_the_cooldown_and_allowed_once_the_handed_clock_has_passed_it()
    {
        var clock = new Stopped(Then);
        using var host = new StoppableHost();
        var runner = new ProofRunner(
            new Backends([FakeBackend.Named("sql", "SQLite"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql"),
            new ProofClients(() => throw new InvalidOperationException("this run never gets as far as a request")),
            new ProofRequests(new SqlRingBuffer(10), new StoreRingBuffer(10), clock),
            host,
            clock);

        host.Stop();
        Assert.True(runner.TryStart(1));
        while (runner.Running)
        {
            await Task.Delay(10);
        }

        // The stamp on the run that just failed is the handed clock's moment, not the real one.
        Assert.Equal(Then, runner.Last!.FinishedAt);
        Assert.False(runner.TryStart(1), "a second run inside the cooldown should be refused");

        clock.Now = Then.Add(ProofRunner.Cooldown).AddSeconds(1);
        Assert.True(runner.TryStart(1), "the cooldown has passed on the clock the runner was handed");
        while (runner.Running)
        {
            await Task.Delay(10);
        }
    }
    // #endregion kept-for-a-window

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
