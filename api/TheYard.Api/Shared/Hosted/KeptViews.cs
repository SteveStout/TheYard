// The views an Admin route answers from, kept in this process (ADR: One frame for every resource, the
// addendum on building behind the request, and the addendum that gave the plan, the Azure card and the runs
// route the same keeping). A view already built answers the request while it is under two minutes old; past
// one minute the request still answers at once and the next view is built behind it, one build a slot at a
// time, shared by every request that waits on it and by the keeper; and ViewKeeper builds again, once a minute,
// every slot somebody asked for in the last hour. A read waits only when no view under two minutes old is held:
// the first read of a slot, or one after an hour without requests dropped it. One mechanism, so every route that
// keeps its views keeps them the same way.

namespace TheYard.Api.Shared.Hosted;

// #region kept-views
/// <summary>The ages every kept view is judged by, the same for each route that keeps its views.</summary>
public static class KeptViews
{
    /// <summary>How old a view is before a read builds its next one behind the answer.</summary>
    public static readonly TimeSpan Behind = TimeSpan.FromMinutes(1);

    /// <summary>How old a built view may be and still answer while its next is built: under the three minutes a minute card reads unread after.</summary>
    public static readonly TimeSpan Served = TimeSpan.FromMinutes(2);

    /// <summary>How long a slot nobody has asked for is still kept built by the keeper.</summary>
    public static readonly TimeSpan Watched = TimeSpan.FromHours(1);
}

/// <summary>What the keeper asks of every route's kept views: build again what was asked for this hour.</summary>
public interface IKeptViews
{
    /// <summary>What the views are of, for the keeper's log line.</summary>
    string Name { get; }

    /// <summary>Builds again every slot asked for in the last hour whose view is a minute old; drops the rest.</summary>
    /// <param name="cancellation">The keeper's stopping.</param>
    Task KeepWatchedAsync(CancellationToken cancellation);
}

/// <summary>
/// One route's views by slot: a view under two minutes old answers at once, with its next built behind it past
/// a minute; a slot with no such view is built while the request waits.
/// </summary>
/// <typeparam name="TView">What the route answers with.</typeparam>
/// <param name="name">What the views are of.</param>
/// <param name="clock">The clock a view's age is measured on.</param>
/// <param name="stopping">The host's stopping, which ends a build behind a request.</param>
/// <param name="keep">Whether a built view is kept; null keeps every one. A view not kept answers its own request and nothing else.</param>
public sealed class KeptViews<TView>(string name, TimeProvider clock, CancellationToken stopping = default, Func<TView, bool>? keep = null) : IKeptViews
    where TView : class
{
    private readonly object _gate = new();

    /// <summary>The newest view of each slot, with when its build began.</summary>
    private readonly Dictionary<string, (DateTimeOffset At, TView View)> _cached = new(StringComparer.Ordinal);

    /// <summary>Each slot asked for, with how to build it and when it was last asked.</summary>
    private readonly Dictionary<string, (Func<CancellationToken, Task<TView>> Build, DateTimeOffset Asked)> _watched = new(StringComparer.Ordinal);

    /// <summary>Every build under way, one a slot, shared by the requests that wait on it, the refresh behind an answer and the keeper.</summary>
    private readonly Dictionary<string, Task<TView>> _building = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public string Name => name;

    /// <summary>The clock the views are aged on, which the route's build reads too.</summary>
    public TimeProvider Clock => clock;

    /// <summary>The slot's view: a kept one under two minutes old at once, with its next built behind it past a minute; else the slot's one build, which the request waits on.</summary>
    /// <param name="slot">The slot, which names everything the build reads.</param>
    /// <param name="build">Builds the slot's view for a request that waits on it.</param>
    /// <param name="cancellation">The request's cancellation, which ends only its own wait: the build goes on and is kept for the next request.</param>
    /// <param name="refresh">Builds the slot's view behind an answer and for the keeper; null builds it as <paramref name="build"/> does.</param>
    public async Task<TView> ReadAsync(string slot, Func<CancellationToken, Task<TView>> build, CancellationToken cancellation, Func<CancellationToken, Task<TView>>? refresh = null)
    {
        var now = clock.GetUtcNow();
        Task<TView> waiting;
        lock (_gate)
        {
            _watched[slot] = (refresh ?? build, now);
            if (_cached.TryGetValue(slot, out var hit) && now - hit.At < KeptViews.Served)
            {
                if (now - hit.At >= KeptViews.Behind)
                {
                    _ = Building(slot, refresh ?? build);
                }
                return hit.View;
            }
            waiting = Building(slot, build);
        }
        return await waiting.WaitAsync(cancellation);
    }

    /// <inheritdoc/>
    public async Task KeepWatchedAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        List<(string Slot, Func<CancellationToken, Task<TView>> Build)> due;
        lock (_gate)
        {
            foreach (string gone in _watched.Where(entry => now - entry.Value.Asked > KeptViews.Watched).Select(entry => entry.Key).ToList())
            {
                _watched.Remove(gone);
                _cached.Remove(gone);
            }
            due = [.. _watched.Where(entry => !_cached.TryGetValue(entry.Key, out var hit) || now - hit.At >= KeptViews.Behind)
                .Select(entry => (entry.Key, entry.Value.Build))];
        }
        foreach (var (slot, build) in due)
        {
            Task<TView> building;
            lock (_gate)
            {
                building = Building(slot, build);
            }
            try
            {
                await building.WaitAsync(cancellation);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                // A slot that cannot be built now keeps the view it has; the next minute asks again.
            }
        }
    }

    /// <summary>Every build under way, finished, whether it succeeded or not: the tests' way to read what a build left.</summary>
    public Task BuiltBehind()
    {
        lock (_gate)
        {
            return Task.WhenAll(_building.Values.Select(task => task.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)).ToList());
        }
    }

    /// <summary>The slot's build under way, or a new one when there is none; called under the gate, so a slot never has two.</summary>
    private Task<TView> Building(string slot, Func<CancellationToken, Task<TView>> build)
    {
        if (_building.TryGetValue(slot, out var running))
        {
            return running;
        }
        var started = Task.Run(() => BuildOnceAsync(slot, build), CancellationToken.None);
        _building[slot] = started;
        // A build nobody waits on fails quietly: the view that answered stays until one succeeds.
        _ = started.ContinueWith(task => task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return started;
    }

    /// <summary>One build of the slot, under two minutes and the host's stopping, gone from the builds under way when it ends.</summary>
    private async Task<TView> BuildOnceAsync(string slot, Func<CancellationToken, Task<TView>> build)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        limit.CancelAfter(KeptViews.Served);
        try
        {
            return await BuildAsync(slot, build, limit.Token);
        }
        finally
        {
            lock (_gate)
            {
                _building.Remove(slot);
            }
        }
    }

    /// <summary>The slot built now and, when it is one to keep and no newer build has been kept, held for the next request under the moment its build began.</summary>
    private async Task<TView> BuildAsync(string slot, Func<CancellationToken, Task<TView>> build, CancellationToken cancellation)
    {
        var at = clock.GetUtcNow();
        var view = await build(cancellation);
        if (keep is null || keep(view))
        {
            lock (_gate)
            {
                // An older build that ends after a newer one never replaces the newer view.
                if (!_cached.TryGetValue(slot, out var held) || held.At <= at)
                {
                    _cached[slot] = (at, view);
                }
            }
        }
        return view;
    }
}
// #endregion kept-views
