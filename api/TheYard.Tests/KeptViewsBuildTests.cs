using System.Reflection;
using System.Runtime.CompilerServices;

namespace TheYard.Tests;

/// <summary>
/// How the kept views run their builds (ADR: One frame for every resource, the addendum on the keeper's pass and a
/// request's own budget): the keeper starts every due slot before it waits on any, so one slow window cannot hold the
/// others past their two minutes; a request that joined a refresh the route refused builds again on its own budget; a
/// later build's view replaces the one before it; and every async step takes a cancellation token. Builds are parked
/// on gates the test opens, so a case says what overlapped rather than only what came out.
/// </summary>
public sealed class KeptViewsBuildTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 16, 0, 0, TimeSpan.Zero);

    /// <summary>A view that says when its build began and whether the route would keep it.</summary>
    private sealed record Stamp(DateTimeOffset At, bool Kept = true);

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task The_keeper_starts_every_due_slot_before_it_waits_on_any()
    {
        var clock = new MovableClock(Start);
        var slow = Gate();
        var otherBegan = Gate();
        int slowBuilds = 0, otherBuilds = 0;
        var views = new KeptViews<Stamp>("test", clock);
        await views.ReadAsync("slow", async cancellation =>
        {
            if (Interlocked.Increment(ref slowBuilds) > 1)
            {
                await slow.Task.WaitAsync(cancellation);
            }
            return new Stamp(clock.Now);
        }, CancellationToken.None);
        await views.ReadAsync("other", cancellation =>
        {
            if (Interlocked.Increment(ref otherBuilds) > 1)
            {
                otherBegan.TrySetResult();
            }
            return Task.FromResult(new Stamp(clock.Now));
        }, CancellationToken.None);
        clock.Now = Start.AddSeconds(61);

        var keeping = views.KeepWatchedAsync(CancellationToken.None);
        bool began = await otherBegan.Task.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(task => task.IsCompletedSuccessfully, TaskScheduler.Default);
        slow.SetResult();
        await keeping;

        Assert.True(began, "the keeper waited on a slow window before it began the next");
    }

    [Fact]
    public async Task A_request_that_joined_a_refused_refresh_builds_again_on_its_own_budget()
    {
        var clock = new MovableClock(Start);
        var refused = Gate();
        var views = new KeptViews<Stamp>("test", clock, keep: view => view.Kept);
        Func<CancellationToken, Task<Stamp>> request = _ => Task.FromResult(new Stamp(clock.Now));
        Func<CancellationToken, Task<Stamp>> refresh = async cancellation =>
        {
            await refused.Task.WaitAsync(cancellation);
            return new Stamp(clock.Now, Kept: false);
        };
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start.AddSeconds(90);
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start + KeptViews.Served + TimeSpan.FromSeconds(10);

        var waiting = views.ReadAsync("slot", request, CancellationToken.None, refresh);
        refused.SetResult();
        var answered = await waiting;

        Assert.True(answered.Kept, "the request answered with the refresh the route refused");
    }

    [Fact]
    public async Task A_request_never_answers_with_a_refresh_the_keeper_began_after_the_one_it_joined_was_refused()
    {
        var clock = new MovableClock(Start);
        var (first, second) = (Gate(), Gate());
        var secondBegan = Gate();
        int refreshes = 0, refusedChecks = 0;
        Task keeping = Task.CompletedTask;
        KeptViews<Stamp>? views = null;
        views = new KeptViews<Stamp>("test", clock, keep: view =>
        {
            // The second look at a refused view is the request's own, after its refresh ended: the keeper begins the
            // next refresh then, before the request takes the gate again.
            if (!view.Kept && Interlocked.Increment(ref refusedChecks) == 2)
            {
                keeping = views!.KeepWatchedAsync(CancellationToken.None);
            }
            return view.Kept;
        });
        Func<CancellationToken, Task<Stamp>> request = _ => Task.FromResult(new Stamp(clock.Now));
        Func<CancellationToken, Task<Stamp>> refresh = async cancellation =>
        {
            int number = Interlocked.Increment(ref refreshes);
            if (number == 2)
            {
                secondBegan.TrySetResult();
            }
            await (number == 1 ? first : second).Task.WaitAsync(cancellation);
            return new Stamp(clock.Now, Kept: false);
        };
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start.AddSeconds(90);
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start + KeptViews.Served + TimeSpan.FromSeconds(10);

        var waiting = views.ReadAsync("slot", request, CancellationToken.None, refresh);
        first.SetResult();
        await secondBegan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        second.SetResult();
        var answered = await waiting;
        await keeping;

        Assert.True(answered.Kept, "the request answered with a second refresh the route refused");
    }

    [Fact]
    public async Task A_second_request_that_joined_a_refused_refresh_answers_the_view_the_first_built_again()
    {
        var clock = new MovableClock(Start);
        var refused = Gate();
        int requestBuilds = 0, refusedChecks = 0;
        Task<Stamp>? one = null, two = null;
        var views = new KeptViews<Stamp>("test", clock, keep: view =>
        {
            // The third look at the refused view is the later of the two requests: it resumes only once the other has
            // built the slot again and answered.
            if (!view.Kept && Interlocked.Increment(ref refusedChecks) == 3)
            {
                SpinWait.SpinUntil(() => one!.IsCompleted || two!.IsCompleted, TimeSpan.FromSeconds(5));
            }
            return view.Kept;
        });
        Func<CancellationToken, Task<Stamp>> request = _ =>
        {
            Interlocked.Increment(ref requestBuilds);
            return Task.FromResult(new Stamp(clock.Now));
        };
        Func<CancellationToken, Task<Stamp>> refresh = async cancellation =>
        {
            await refused.Task.WaitAsync(cancellation);
            return new Stamp(clock.Now, Kept: false);
        };
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start.AddSeconds(90);
        await views.ReadAsync("slot", request, CancellationToken.None, refresh);
        clock.Now = Start + KeptViews.Served + TimeSpan.FromSeconds(10);

        one = views.ReadAsync("slot", request, CancellationToken.None, refresh);
        two = views.ReadAsync("slot", request, CancellationToken.None, refresh);
        refused.SetResult();
        await Task.WhenAll(one, two);

        Assert.Equal(2, requestBuilds);
        Assert.Equal((await one).At, (await two).At);
    }

    [Fact]
    public async Task A_later_build_replaces_the_view_the_earlier_one_kept()
    {
        var clock = new MovableClock(Start);
        var second = Gate();
        int begun = 0;
        var views = new KeptViews<Stamp>("test", clock);
        Func<CancellationToken, Task<Stamp>> build = async cancellation =>
        {
            var at = clock.Now;
            if (Interlocked.Increment(ref begun) == 2)
            {
                await second.Task.WaitAsync(cancellation);
            }
            return new Stamp(at);
        };
        var first = await views.ReadAsync("slot", build, CancellationToken.None);
        clock.Now = Start + KeptViews.Served + TimeSpan.FromSeconds(10);

        var waiting = views.ReadAsync("slot", build, CancellationToken.None);
        Assert.False(waiting.IsCompleted, "the request past two minutes was answered without a build");
        second.SetResult();
        var answered = await waiting;
        clock.Now += TimeSpan.FromSeconds(10);
        var served = await views.ReadAsync("slot", build, CancellationToken.None);

        Assert.Equal(2, begun);
        Assert.True(answered.At > first.At, "the second request answered the first build's view");
        Assert.Equal(answered.At, served.At);
    }

    [Fact]
    public void Every_async_step_of_the_kept_views_takes_a_cancellation_token()
    {
        var missing = typeof(KeptViews<>).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.IsDefined(typeof(AsyncStateMachineAttribute)))
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
            .Select(method => method.Name)
            .ToList();

        Assert.True(missing.Count == 0, "async with no CancellationToken: " + string.Join(", ", missing));
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_read_whose_request_is_already_cancelled_stops_and_starts_no_build()
    {
        // A kept view or a build that ends at once must not answer a request that has already gone: the read stops
        // first (CloudTimeoutTests' own case failed in CI on 1.0.5.42's main when the build won the race).
        var clock = new MovableClock(Start);
        int builds = 0;
        var views = new KeptViews<Stamp>("test", clock);
        Task<Stamp> Build(CancellationToken cancellation)
        {
            Interlocked.Increment(ref builds);
            return Task.FromResult(new Stamp(clock.Now));
        }
        await views.ReadAsync("kept", Build, CancellationToken.None);
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => views.ReadAsync("kept", Build, gone.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => views.ReadAsync("empty", Build, gone.Token));

        Assert.Equal(1, builds);
    }
}
