using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The keep-warm loop (ADR: Kept awake): its first pass waits the stagger, then
/// one pass every four minutes; a pass that runs long pushes the next back and
/// never overlaps it; a pass that throws is a warning and the next still comes;
/// and inside a pass a read that fails is counted and the rest are still sent.
/// The clock is one the test moves by hand.
/// </summary>
public sealed class KeepWarmTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 17, 0, 0, TimeSpan.Zero);

    // #region keep-warm-tests
    [Fact]
    public async Task The_first_pass_waits_the_stagger_and_then_one_pass_runs_every_four_minutes()
    {
        var clock = new ManualClock(Start);
        int passes = 0;
        var loop = new KeepWarm(
            _ =>
            {
                Interlocked.Increment(ref passes);
                return Task.FromResult(Pass(clock));
            },
            clock,
            TimeSpan.FromSeconds(30),
            NullLogger.Instance);
        await loop.StartAsync(CancellationToken.None);
        // The loop runs on a thread of its own, so each wait is read as set before the clock moves.
        await Until(() => clock.Pending == 1);

        await clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, Volatile.Read(ref passes));
        await clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => Volatile.Read(ref passes) == 1 && clock.Pending == 1);

        await clock.Advance(TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref passes));
        await clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => Volatile.Read(ref passes) == 2 && clock.Pending == 1);
        await clock.Advance(TimeSpan.FromMinutes(4));
        await Until(() => Volatile.Read(ref passes) == 3);

        await loop.StopAsync(CancellationToken.None);
        Assert.Equal(3, loop.Passes);
    }

    [Fact]
    public async Task A_pass_that_runs_long_pushes_the_next_back_and_two_never_run_at_once()
    {
        var clock = new ManualClock(Start);
        int running = 0;
        int most = 0;
        int started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new KeepWarm(
            async _ =>
            {
                int now = Interlocked.Increment(ref running);
                InterlockedMax(ref most, now);
                if (Interlocked.Increment(ref started) == 1)
                {
                    await release.Task;
                    clock.Set(Start + TimeSpan.FromMinutes(10));
                }

                Interlocked.Decrement(ref running);
                return Pass(clock);
            },
            clock,
            TimeSpan.Zero,
            NullLogger.Instance);
        await loop.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref started) == 1);

        // Ten minutes pass while the first is still running: no second pass starts beside it.
        await clock.Advance(TimeSpan.FromMinutes(9));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref started));

        // It took longer than the interval, so the next starts as soon as it ends.
        release.SetResult();
        await Until(() => Volatile.Read(ref started) == 2);
        Assert.Equal(1, Volatile.Read(ref most));
        Assert.Equal(TimeSpan.Zero, KeepWarm.Next(Start, Start + TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(3), KeepWarm.Next(Start, Start + TimeSpan.FromMinutes(1)));

        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_pass_that_throws_is_logged_and_the_next_one_still_comes()
    {
        var clock = new ManualClock(Start);
        int calls = 0;
        var loop = new KeepWarm(
            _ => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<KeepWarmPass>(new HttpRequestException("the store is asleep"))
                : Task.FromResult(Pass(clock)),
            clock,
            TimeSpan.Zero,
            NullLogger.Instance);
        await loop.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref calls) == 1 && clock.Pending == 1);
        Assert.Null(loop.Last);

        await clock.Advance(TimeSpan.FromMinutes(4));
        await Until(() => loop.Last is not null);
        Assert.Equal(2, Volatile.Read(ref calls));

        await loop.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Inside_a_pass_a_read_that_fails_is_counted_and_every_other_read_is_still_sent()
    {
        var handler = new Answers();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") };

        var pass = await KeepWarmReads.RunAsync(client, ["sql", "cosmos"], new ManualClock(Start), NullLogger.Instance, CancellationToken.None);

        // Every path on both stores, and one vehicle from each store's listing.
        Assert.Equal((KeepWarmReads.Paths.Length + 1) * 2, pass.Reads);
        // /api/health answers 503 and /api/admin/azure throws: two reads per store.
        Assert.Equal(4, pass.Failed);
        Assert.Contains("/api/vehicles/abc-001", handler.Seen);
        Assert.All(handler.Agents, agent => Assert.Equal(KeepWarm.Agent, agent));
        Assert.Equal(new[] { "cosmos", "sql" }, handler.Stores.Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_marks_are_the_site_reading_itself()
    {
        Assert.True(Hits.IsSelf(KeepWarm.Agent));
        Assert.Null(KeepWarmReads.FirstVehicle("not json"));
        Assert.Equal("abc-001", KeepWarmReads.FirstVehicle("""{"items":[{"id":"abc-001"}],"total":1}"""));
    }
    // #endregion keep-warm-tests

    private static KeepWarmPass Pass(TimeProvider clock) => new(clock.GetUtcNow(), 1, 0, 1, "/api/health (sql)");

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
            // Another thread moved it first; read it again.
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int tries = 0; tries < 300 && !condition(); tries++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "the loop did not get there in three seconds");
    }

    /// <summary>A listing with one vehicle, a health report that says 503, an Azure read that throws, and 200 for the rest.</summary>
    private sealed class Answers : HttpMessageHandler
    {
        public List<string> Seen { get; } = [];

        public List<string> Agents { get; } = [];

        public List<string> Stores { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery;
            lock (Seen)
            {
                Seen.Add(path);
                Agents.Add(request.Headers.UserAgent.ToString());
                Stores.Add(request.Headers.GetValues(Backends.HeaderName).Single());
            }

            if (path == "/api/admin/azure")
            {
                throw new HttpRequestException("no answer");
            }

            var response = new HttpResponseMessage(path == "/api/health" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(path == "/api/vehicles" ? """{"items":[{"id":"abc-001"}]}""" : "{}"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>A clock the test moves by hand, with timers that fire when it passes their time.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_timers)
            {
                return _now;
            }
        }

        /// <summary>How many timers are waiting on this clock.</summary>
        public int Pending
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
                }
            }
        }

        public void Set(DateTimeOffset now)
        {
            lock (_timers)
            {
                _now = now;
            }
        }

        public async Task Advance(TimeSpan by)
        {
            List<Timer> due;
            lock (_timers)
            {
                _now += by;
                due = _timers.Where(timer => timer.Due is { } at && at <= _now).ToList();
                foreach (var timer in due)
                {
                    timer.Due = null;
                    _timers.Remove(timer);
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }

            // Let the continuations the timers released run.
            await Task.Delay(50);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(this);
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                    if (Due is not null)
                    {
                        clock._timers.Add(this);
                    }
                }

                return true;
            }

            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
