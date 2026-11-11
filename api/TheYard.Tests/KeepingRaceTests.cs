using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// What the kept views must hold when builds overlap (ADR: One frame for every resource, the addendum on one build a
/// slot): a slot has one build at a time whoever asks for it, the requests and the keeper alike; a view once answered
/// is never followed by an older one; one slow route does not hold up the keeper's other routes; and the keeper's
/// reads of the run histories leave room in the minute for a page a request asks for that is not kept. Each case
/// parks its builds on gates the test opens, so it says what overlapped rather than only what came out.
/// </summary>
public sealed class KeepingRaceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    /// <summary>A view that says when its build began.</summary>
    private sealed record Stamp(DateTimeOffset At);

    /// <summary>A build that counts itself, stamps the clock it began on, and waits on the gate of its own number; a missing gate is an open one.</summary>
    private sealed class Builds(MovableClock clock, params TaskCompletionSource[] gates)
    {
        private int _begun;

        public int Begun => Volatile.Read(ref _begun);

        public async Task<Stamp> BuildAsync(CancellationToken cancellation)
        {
            var at = clock.GetUtcNow();
            int number = Interlocked.Increment(ref _begun);
            if (number <= gates.Length)
            {
                await gates[number - 1].Task.WaitAsync(cancellation);
            }
            return new Stamp(at);
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Two_requests_that_miss_the_same_slot_share_one_build()
    {
        var clock = new MovableClock(Start);
        var gate = Gate();
        var builds = new Builds(clock, gate);
        var views = new KeptViews<Stamp>("test", clock);

        var first = views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        var second = views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, builds.Begun);
    }

    [Fact]
    public async Task The_keeper_joins_a_build_a_request_started_behind_its_answer()
    {
        var clock = new MovableClock(Start);
        var open = Gate();
        open.SetResult();
        var behind = Gate();
        var builds = new Builds(clock, open, behind);
        var views = new KeptViews<Stamp>("test", clock);
        await views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        clock.Now = Start.AddSeconds(90);

        await views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        var keeping = views.KeepWatchedAsync(CancellationToken.None);
        behind.SetResult();
        await keeping;
        await views.BuiltBehind();

        Assert.Equal(2, builds.Begun);
    }

    [Fact]
    public async Task A_view_once_answered_is_never_followed_by_an_older_one()
    {
        var clock = new MovableClock(Start);
        var (older, newer) = (Gate(), Gate());
        var builds = new Builds(clock, older, newer);
        var views = new KeptViews<Stamp>("test", clock);

        var first = views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        clock.Now = Start.AddSeconds(10);
        var second = views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);
        newer.SetResult();
        var answered = await second.WaitAsync(TimeSpan.FromSeconds(1)).ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : null, TaskScheduler.Default);
        older.SetResult();
        answered ??= await second;
        await first;
        clock.Now = Start.AddSeconds(20);

        var served = await views.ReadAsync("slot", builds.BuildAsync, CancellationToken.None);

        Assert.True(served.At >= answered.At, $"served the view built at {served.At:T} after answering the one built at {answered.At:T}");
    }

    [Fact]
    public async Task One_slow_route_does_not_hold_up_the_keeper_s_other_routes()
    {
        var fast = new Watched("fast");
        using var keeper = new ViewKeeper([new Stuck(), fast], TimeProvider.System, NullLogger<ViewKeeper>.Instance);

        await keeper.StartAsync(CancellationToken.None);
        bool kept = await fast.Kept.Task.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(task => task.IsCompletedSuccessfully, TaskScheduler.Default);
        await keeper.StopAsync(CancellationToken.None);

        Assert.True(kept, "the route behind a slow one was not kept");
    }

    [Fact]
    public async Task The_keeper_leaves_room_in_the_minute_for_a_runs_page_a_request_asks_for()
    {
        var clock = new MovableClock(Start);
        var inner = new CountingRuns();
        var runs = new GuardedProcessRuns(inner, clock);
        var merger = FunctionProcesses.Find("Merger")!;
        for (int page = 0; page < GuardedProcessRuns.FreshReadsAMinute; page++)
        {
            await runs.RunsAsync(merger, TimeSpan.FromDays(7), page * 20, 20, CancellationToken.None);
        }
        clock.Now = Start.AddSeconds(61);

        await runs.Views.KeepWatchedAsync(CancellationToken.None);
        var asked = await runs.RunsAsync(merger, TimeSpan.FromDays(7), GuardedProcessRuns.FreshReadsAMinute * 20, 20, CancellationToken.None);

        Assert.True(asked.Available, asked.Note);
    }

    /// <summary>A route whose keeping never finishes until the keeper stops.</summary>
    private sealed class Stuck : IKeptViews
    {
        public string Name => "stuck";

        public Task KeepWatchedAsync(CancellationToken cancellation) => Task.Delay(Timeout.Infinite, cancellation);
    }

    /// <summary>A route that says when the keeper first reached it.</summary>
    private sealed class Watched(string name) : IKeptViews
    {
        public TaskCompletionSource Kept { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => name;

        public Task KeepWatchedAsync(CancellationToken cancellation)
        {
            Kept.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>The workspace, answering every page at once and counting the reads.</summary>
    private sealed class CountingRuns : IProcessRunsReader
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(new ProcessRunsRead(true, null, [], ProcessTally.None));
        }

        public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(ProcessFold.Unread("test"));

        public string? TraceHref(string operationId) => null;

        public string? ProcessHref(string process) => null;
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
