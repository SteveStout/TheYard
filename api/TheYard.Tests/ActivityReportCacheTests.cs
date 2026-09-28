using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The public activity report is kept a short while per window and rebuilt behind
/// the next read, so a reader never waits on the visitor rows being counted (ADR:
/// Site activity, and the line an address does not cross, addendum of 28
/// September): fresh for thirty seconds, served while a new one is built for ten
/// minutes after that, built while the caller waits only past that, and one build
/// per window however many callers ask.
/// </summary>
public sealed class ActivityReportCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 17, 0, 0, TimeSpan.Zero);

    // #region report-cache
    [Fact]
    public async Task A_report_younger_than_thirty_seconds_is_served_without_building_another()
    {
        var clock = new MovableClock { Now = Start };
        var cache = new ActivityReportCache(clock);
        var builds = new Builds();

        object first = await cache.GetAsync("7d", builds.Next, CancellationToken.None);
        clock.Now += TimeSpan.FromSeconds(29);
        object second = await cache.GetAsync("7d", builds.Next, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, builds.Count);
    }

    [Fact]
    public async Task An_older_report_is_served_at_once_and_rebuilt_behind_it()
    {
        var clock = new MovableClock { Now = Start };
        var cache = new ActivityReportCache(clock);
        var builds = new Builds();
        object first = await cache.GetAsync("7d", builds.Next, CancellationToken.None);

        clock.Now += TimeSpan.FromMinutes(4);
        object served = await cache.GetAsync("7d", builds.Next, CancellationToken.None);
        Assert.Same(first, served);

        await builds.Settled(2);
        object after = await cache.GetAsync("7d", builds.Next, CancellationToken.None);
        Assert.NotSame(first, after);
        Assert.Equal(2, builds.Count);
    }

    [Fact]
    public async Task Past_ten_minutes_the_caller_waits_for_a_new_report_and_each_window_is_its_own()
    {
        var clock = new MovableClock { Now = Start };
        var cache = new ActivityReportCache(clock);
        var builds = new Builds();
        object week = await cache.GetAsync("7d", builds.Next, CancellationToken.None);
        object month = await cache.GetAsync("30d", builds.Next, CancellationToken.None);
        Assert.NotSame(week, month);

        clock.Now += TimeSpan.FromMinutes(11);
        object fresh = await cache.GetAsync("7d", builds.Next, CancellationToken.None);

        Assert.NotSame(week, fresh);
        Assert.Equal(3, builds.Count);
    }

    [Fact]
    public async Task Callers_who_arrive_together_share_one_build()
    {
        var cache = new ActivityReportCache(new MovableClock { Now = Start });
        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        Task<object> Slow(DateTimeOffset now, CancellationToken cancellation)
        {
            Interlocked.Increment(ref count);
            return gate.Task;
        }

        var waiting = Enumerable.Range(0, 8).Select(_ => cache.GetAsync("24h", Slow, CancellationToken.None)).ToList();
        var report = new object();
        gate.SetResult(report);
        var answers = await Task.WhenAll(waiting);

        Assert.All(answers, answer => Assert.Same(report, answer));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_rebuild_that_fails_leaves_the_report_it_was_replacing_in_place()
    {
        var clock = new MovableClock { Now = Start };
        var cache = new ActivityReportCache(clock);
        object first = await cache.GetAsync("7d", (_, _) => Task.FromResult(new object()), CancellationToken.None);

        clock.Now += TimeSpan.FromMinutes(2);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        object served = await cache.GetAsync(
            "7d",
            (_, _) =>
            {
                failed.TrySetResult();
                return Task.FromException<object>(new InvalidOperationException("the store is asleep"));
            },
            CancellationToken.None);
        await failed.Task;
        await Task.Delay(50);

        Assert.Same(first, served);
        Assert.Same(first, await cache.GetAsync("7d", (_, _) => Task.FromResult(new object()), CancellationToken.None));
    }
    // #endregion report-cache

    /// <summary>Builds that count themselves, each a new object, and can be waited for.</summary>
    private sealed class Builds
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<object> Next(DateTimeOffset now, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new object());
        }

        public async Task Settled(int count)
        {
            for (int tries = 0; tries < 100 && Count < count; tries++)
            {
                await Task.Delay(10);
            }

            // The build has been called; give its entry a moment to land.
            await Task.Delay(50);
        }
    }

    /// <summary>A clock the test moves by hand, as PasswordResetTests' is.</summary>
    private sealed class MovableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
