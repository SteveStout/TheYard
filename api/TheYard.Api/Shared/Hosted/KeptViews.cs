// The views an Admin route answers from, kept in this process (ADR: One frame for every resource, the
// addendum on building behind the request, and the addendum that gave the plan, the Azure card and the runs
// route the same keeping). A view already built answers the request while it is under two minutes old; past
// one minute the request still answers at once and the next view is built behind it, one build a slot at a
// time; and ViewKeeper builds again, once a minute, every slot somebody asked for in the last hour. Only the
// first read of a slot, with nothing built yet, waits for its build. One mechanism, so every route that keeps
// its views keeps them the same way.

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

    /// <summary>The builds running behind a request, one a slot.</summary>
    private readonly Dictionary<string, Task> _building = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public string Name => name;

    /// <summary>The clock the views are aged on, which the route's build reads too.</summary>
    public TimeProvider Clock => clock;

    /// <summary>The slot's view: a kept one under two minutes old at once, with its next built behind it past a minute; else a build the request waits on.</summary>
    /// <param name="slot">The slot, which names everything the build reads.</param>
    /// <param name="build">Builds the slot's view.</param>
    /// <param name="cancellation">The request's cancellation, which ends only a build the request waits on.</param>
    public async Task<TView> ReadAsync(string slot, Func<CancellationToken, Task<TView>> build, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            _watched[slot] = (build, now);
            if (_cached.TryGetValue(slot, out var hit) && now - hit.At < KeptViews.Served)
            {
                if (now - hit.At >= KeptViews.Behind && !_building.ContainsKey(slot))
                {
                    _building[slot] = Task.Run(() => BuildBehindAsync(slot, build, stopping), CancellationToken.None);
                }
                return hit.View;
            }
        }
        return await BuildAsync(slot, build, cancellation);
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
            try
            {
                await BuildAsync(slot, build, cancellation);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                // A slot that cannot be built now keeps the view it has; the next minute asks again.
            }
        }
    }

    /// <summary>Every build running behind a request, finished: the tests' way to read what a build behind left.</summary>
    public Task BuiltBehind()
    {
        lock (_gate)
        {
            return Task.WhenAll(_building.Values.ToList());
        }
    }

    /// <summary>One build behind a request, under two minutes and the host's stopping; a failure keeps the view that answered.</summary>
    private async Task BuildBehindAsync(string slot, Func<CancellationToken, Task<TView>> build, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(KeptViews.Served);
        try
        {
            await BuildAsync(slot, build, limit.Token);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            // Nobody waits on this build: the view that answered stays until one succeeds.
        }
        finally
        {
            lock (_gate)
            {
                _building.Remove(slot);
            }
        }
    }

    /// <summary>The slot built now and, when it is one to keep, held for the next request under the moment its build began.</summary>
    private async Task<TView> BuildAsync(string slot, Func<CancellationToken, Task<TView>> build, CancellationToken cancellation)
    {
        var at = clock.GetUtcNow();
        var view = await build(cancellation);
        if (keep is null || keep(view))
        {
            lock (_gate)
            {
                _cached[slot] = (at, view);
            }
        }
        return view;
    }
}
// #endregion kept-views
