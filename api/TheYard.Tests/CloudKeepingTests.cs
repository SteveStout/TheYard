using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The card route answers from a view already built, and Azure is asked behind it (ADR: One frame for every resource,
/// the addendum of 1.0.4.164). The Admin soak read every Cloud card at 0.3 s from its one-minute cache and at 2 to 15 s
/// whenever the cache had lapsed, because the request then waited on the build and its live read. Here the build is
/// held on a gate the test opens, so a case can say the request did not wait on it rather than only that it was quick.
/// </summary>
public sealed class CloudKeepingTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);

    private static ResourceCard Card() => new(
        "insights", "insights", "appi-test", ["ingest_today_gb"],
        (_, _, _, _) => Task.FromResult<IReadOnlyList<ResourceMinute>>([]),
        (_, _, _) => throw new InvalidOperationException("no recorder here, so no hour is read"),
        inputs => InsightsCard.View(inputs));

    /// <summary>A reader whose every build after the first waits on <paramref name="gate"/>; <paramref name="builds"/> counts the builds begun.</summary>
    private static CloudReader Reader(MovableClock clock, TaskCompletionSource gate, Func<int> builds)
    {
        var keep = new MemoryResourceHistory("kept in the test");
        return new CloudReader([(Card(), keep)], null, async (_, cancellation) =>
        {
            if (builds() > 1)
            {
                await gate.Task.WaitAsync(cancellation);
            }
            return null;
        }, clock);
    }

    [Fact]
    public async Task A_view_a_minute_old_answers_at_once_and_its_next_is_built_behind_it()
    {
        var clock = new MovableClock(Start);
        var gate = new TaskCompletionSource();
        int built = 0;
        var reader = Reader(clock, gate, () => ++built);
        var first = await reader.ReadAsync("insights", "24h", CancellationToken.None);
        clock.Now = Start.AddSeconds(90);

        var read = reader.ReadAsync("insights", "24h", CancellationToken.None);

        Assert.True(read.IsCompleted, "the request waited on the build behind it");
        Assert.Equal(first.AsOfMs, (await read).AsOfMs);
        gate.SetResult();
        await reader.Views.BuiltBehind();
        Assert.Equal(2, built);
        Assert.Equal(Start.AddSeconds(90).ToUnixTimeMilliseconds(), (await reader.ReadAsync("insights", "24h", CancellationToken.None)).AsOfMs);
    }

    [Fact]
    public async Task A_view_past_its_two_minutes_is_not_served_and_the_request_waits_for_a_new_one()
    {
        var clock = new MovableClock(Start);
        var gate = new TaskCompletionSource();
        int built = 0;
        var reader = Reader(clock, gate, () => ++built);
        await reader.ReadAsync("insights", "24h", CancellationToken.None);
        clock.Now = Start + KeptViews.Served;

        var read = reader.ReadAsync("insights", "24h", CancellationToken.None);

        Assert.False(read.IsCompleted, "a view the page would call unread was served");
        gate.SetResult();
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (await read).AsOfMs);
    }

    [Fact]
    public async Task The_keeper_builds_a_window_asked_for_this_hour_and_drops_one_nobody_asked_for()
    {
        var clock = new MovableClock(Start);
        var gate = new TaskCompletionSource();
        gate.SetResult();
        int built = 0;
        var reader = Reader(clock, gate, () => ++built);
        await reader.ReadAsync("insights", "24h", CancellationToken.None);

        await reader.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(1, built);
        clock.Now = Start.AddSeconds(61);
        await reader.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(2, built);
        clock.Now = Start + KeptViews.Watched + TimeSpan.FromMinutes(1);
        await reader.Views.KeepWatchedAsync(CancellationToken.None);

        Assert.Equal(2, built);
    }

    [Fact]
    public async Task The_hosted_keeper_builds_what_was_asked_for_without_a_request()
    {
        var clock = new MovableClock(Start);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int built = 0;
        var keep = new MemoryResourceHistory("kept in the test");
        var reader = new CloudReader([(Card(), keep)], null, (_, _) =>
        {
            if (++built == 2)
            {
                second.TrySetResult();
            }
            return Task.FromResult<double?>(null);
        }, clock);
        await reader.ReadAsync("insights", "24h", CancellationToken.None);
        clock.Now = Start.AddSeconds(61);
        using var keeper = new ViewKeeper([reader.Views], clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ViewKeeper>.Instance);

        await keeper.StartAsync(CancellationToken.None);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await keeper.StopAsync(CancellationToken.None);

        Assert.True(built >= 2);
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
