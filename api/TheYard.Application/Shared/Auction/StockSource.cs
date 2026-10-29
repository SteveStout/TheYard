// Which catalogue answers: the store when it holds the whole catalogue, otherwise the expansion in
// memory while the store is filled beside the site or watched for another site's fill. A class of
// its own because it is the part that decides whether this process holds the catalogue (ADR: The
// store does the work), and Stock with it was over three hundred lines (ADR-088).
using TheYard.Data;

namespace TheYard.Application.Shared.Auction;

/// <summary>The choice between the store and a list in memory, and the fill, for <see cref="Stock"/>.</summary>
/// <param name="_store">The store's catalogue, or null to answer from memory.</param>
/// <param name="_seeder">The deterministic expansion, read only to fill the store or to stand in for it.</param>
/// <param name="_upkeep">What this site does about a store that holds less than it should.</param>
/// <param name="switched">Called when the store takes over from the list, so cached answers are let go.</param>
public sealed class StockSource(IVehicleCatalogue? _store, IVehicleSource _seeder, CatalogueUpkeep _upkeep, Action switched)
{
    private volatile IVehicleCatalogue? _active;
    private volatile MemoryCatalogue? _bridge;
    private volatile string? _note;
    private int _filled;
    private IReadOnlyList<Vehicle>? _pending;

    /// <summary>The catalogue that answers, once one is chosen.</summary>
    public IVehicleCatalogue? Active => _active;

    /// <summary>The background fill, while one runs; finished or null otherwise. A test awaits it.</summary>
    public Task? Filling { get; private set; }

    /// <summary>Where the catalogue is answered from right now.</summary>
    public CatalogueReport Report()
    {
        int held = _bridge?.Count ?? 0;
        bool running = Filling is { IsCompleted: false };
        string mode = _active is null ? "not warm"
            : !ReferenceEquals(_active, _bridge) ? "store"
            : running && _upkeep == CatalogueUpkeep.Fill ? "filling"
            : running ? "waiting for the store"
            : "memory";
        return new CatalogueReport(mode, held, _store?.Target ?? held, _store is null ? held : Volatile.Read(ref _filled), _note);
    }

    /// <summary>Chooses the catalogue that answers, once per warm-up.</summary>
    /// <param name="cancellation">
    /// The warming caller's, which the hosted service makes the host's stopping token. It is
    /// carried to the reads this call waits on AND kept by the two loops this call may leave
    /// running behind it, the fill and the watch, because those outlive every request and the
    /// only thing that should ever stop them is the container going down.
    /// </param>
    public async Task<IVehicleCatalogue> ChooseAsync(CancellationToken cancellation) => _active = await PickAsync(cancellation);

    // #region choose
    /// <summary>
    /// The store when it holds the whole catalogue; filled first when what is missing is small
    /// enough to write before answering; otherwise the expansion in memory, with the store filled
    /// beside the site when this site owns the fill. A store that cannot be read at all is the
    /// same as an empty one that is not filled: the site keeps serving, and the Admin tab says why.
    /// </summary>
    private async Task<IVehicleCatalogue> PickAsync(CancellationToken cancellation)
    {
        if (_store is null)
        {
            return Bridge(await _seeder.LoadAsync(cancellation));
        }

        int filled;
        try
        {
            filled = await _store.FilledAsync(cancellation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _note = $"the store's catalogue could not be read ({ex.GetType().Name}), so the expansion answers from memory";
            var unread = Bridge(await _seeder.LoadAsync(cancellation));
            Watch(_store, cancellation);
            return unread;
        }
        Volatile.Write(ref _filled, filled);
        int missing = _store.Target - filled;
        if (missing <= 0)
        {
            return _store;
        }

        var expansion = await _seeder.LoadAsync(cancellation);
        if (missing <= _store.Pace.FillAtWarmUpTo)
        {
            await FillAsync(_store, expansion, filled, pause: false, cancellation);
            return _store;
        }

        var bridge = Bridge(expansion);
        if (_upkeep == CatalogueUpkeep.Fill)
        {
            _note = $"the store holds {filled:N0} of {_store.Target:N0}; filling it beside the site";
            // Handed over through a field rather than captured by the lambda, so once the fill
            // lets go of the list nothing else in this object still holds it.
            _pending = expansion;
            var store = _store;
            Filling = Task.Run(() => FillBesideAsync(store, filled, cancellation), cancellation);
        }
        else
        {
            _note = $"the store holds {filled:N0} of {_store.Target:N0}, and this site does not fill it, so the expansion answers from memory";
            Watch(_store, cancellation);
        }
        return bridge;
    }

    /// <summary>
    /// For a site that does not fill this store: count it once a minute, for a day at most, and
    /// switch to it the minute it holds the whole catalogue, which is when the other site's fill
    /// finishes. Nothing when upkeep is off, which is every test host.
    ///
    /// <para>A day of one-minute counts is the longest-running thing this class starts, and
    /// before this rung nothing could stop it: a container being replaced kept counting until
    /// the process died. It now takes the warm-up's token, which the host makes its own
    /// stopping token, so a shutdown ends the loop at its next wait.</para>
    /// </summary>
    /// <param name="store">The store to count.</param>
    /// <param name="cancellation">The host's, so the loop ends when the container goes down.</param>
    private void Watch(IVehicleCatalogue store, CancellationToken cancellation)
    {
        if (_upkeep == CatalogueUpkeep.Off)
        {
            return;
        }
        Filling = Task.Run(async () =>
        {
            for (int minute = 0; minute < 24 * 60 && !cancellation.IsCancellationRequested; minute++)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                try
                {
                    int filled = await store.FilledAsync(cancellation);
                    Volatile.Write(ref _filled, filled);
                    if (filled >= store.Target)
                    {
                        SwitchTo(store);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _note = $"the store's catalogue could not be counted ({ex.GetType().Name}); counting again in a minute";
                }
            }
        }, cancellation);
    }

    /// <summary>
    /// The switch: the store answers, the list is let go, and one compacting collection hands its
    /// memory back to the plan so the dial shows it the same minute rather than whenever the
    /// collector next decides to give pages back.
    /// </summary>
    private void SwitchTo(IVehicleCatalogue store)
    {
        _active = store;
        _bridge = null;
        _note = null;
        switched();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private MemoryCatalogue Bridge(IReadOnlyList<Vehicle> vehicles)
    {
        var bridge = new MemoryCatalogue(vehicles);
        _bridge = bridge;
        return bridge;
    }
    // #endregion choose

    // #region fill
    /// <summary>Writes the expansion from just before <paramref name="from"/> to the end, a batch at a time at the store's pace.</summary>
    /// <param name="store">The store to write.</param>
    /// <param name="expansion">The whole expansion, read once by the caller.</param>
    /// <param name="from">The count the store last reported.</param>
    /// <param name="pause">Whether to keep the store's pace between batches, which a fill beside a running site does.</param>
    /// <param name="cancellation">The host's; a fill stopped part way is picked up a batch back next time, which is why stopping it is safe.</param>
    private async Task FillAsync(IVehicleCatalogue store, IReadOnlyList<Vehicle> expansion, int from, bool pause, CancellationToken cancellation)
    {
        int batch = Math.Clamp(store.Pace.Batch, 1, 10_000);
        // A batch back from the count, because a fill that stopped mid-batch wrote some of it;
        // every write is an upsert or skips what is there, so going over it again is free of harm.
        int count = Math.Min(store.Target, expansion.Count);
        for (int start = Math.Max(0, from - batch); start < count; start += batch)
        {
            var slice = new List<CatalogueEntry>(Math.Min(batch, count - start));
            for (int seq = start; seq < Math.Min(count, start + batch); seq++)
            {
                slice.Add(new CatalogueEntry(seq, expansion[seq]));
            }
            await store.FillAsync(slice, cancellation);
            Volatile.Write(ref _filled, Math.Max(Volatile.Read(ref _filled), start + slice.Count));
            if (pause && store.Pace.Pause > TimeSpan.Zero)
            {
                await Task.Delay(store.Pace.Pause, cancellation);
            }
        }
    }

    /// <summary>
    /// The fill beside a running site, then the switch. Tried again a minute later when the store
    /// refuses, up to ten times.
    /// </summary>
    /// <param name="store">The store to fill.</param>
    /// <param name="from">The count the store reported at the warm-up.</param>
    /// <param name="cancellation">The host's, so a container being replaced stops filling.</param>
    private async Task FillBesideAsync(IVehicleCatalogue store, int from, CancellationToken cancellation)
    {
        IReadOnlyList<Vehicle> expansion = Interlocked.Exchange(ref _pending, null) ?? [];
        for (int attempt = 1; attempt <= 10 && !cancellation.IsCancellationRequested; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    // Said once the fill is running again, so the Admin tab does not show a refusal
                    // that has already been got past.
                    _note = $"the store holds {from:N0} of {store.Target:N0}; filling it beside the site again after a refused batch";
                }
                await FillAsync(store, expansion, from, pause: true, cancellation);
                int filled = await store.FilledAsync(cancellation);
                Volatile.Write(ref _filled, filled);
                if (filled >= store.Target)
                {
                    break;
                }
                from = filled;
                _note = $"the fill finished with {filled:N0} of {store.Target:N0} counted; filling again";
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _note = $"the fill stopped at {Volatile.Read(ref _filled):N0} of {store.Target:N0} ({ex.GetType().Name}); trying again in a minute";
                from = Volatile.Read(ref _filled);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
            }
        }
        if (Volatile.Read(ref _filled) < store.Target)
        {
            return;
        }

        expansion = [];
        SwitchTo(store);
    }
    // #endregion fill
}
