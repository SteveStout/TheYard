// The warm-up, the choice between the store and a list in memory, and the fill. Its own file
// because it is the part that decides whether this process holds the catalogue (ADR: The store does
// the work).
using TheYard.Data;

namespace TheYard.Application;

/// <summary>The warm-up, the choice between the store and a list in memory, and the fill; the class is described in InventoryService.cs.</summary>
public sealed partial class InventoryService
{
    // #region warm
    // One warm-up, shared by every caller, started by whoever asks first; a warm-up that failed is
    // not kept, so the next caller starts a fresh one (ADR: The ports learn to wait). What it reads
    // is small: the photo manifest, the facets, and one count of what the store holds.
    private readonly object _warmGate = new();
    private Task<Warmed>? _warm;
    private volatile IVehicleCatalogue? _active;
    private volatile MemoryCatalogue? _bridge;
    private volatile string? _note;
    private int _filled;
    private IReadOnlyList<Vehicle>? _pending;

    private sealed record Warmed(IReadOnlyDictionary<string, IReadOnlyList<PhotoEntry>> Pools, InventoryFacets Facets);

    private Task<Warmed> WarmTask()
    {
        lock (_warmGate)
        {
            if (_warm is null || _warm.IsFaulted || _warm.IsCanceled)
            {
                _warm = BuildAsync();
            }
            return _warm;
        }
    }

    /// <summary>Read what the catalogue needs before the first visitor does: the photo pools, the facets, and how full the store is.</summary>
    public Task WarmAsync() => WarmTask();

    /// <summary>Whether the warm-up has finished, which the host asserts before serving.</summary>
    public bool IsWarm => _warm is { IsCompletedSuccessfully: true };

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

    private async Task<Warmed> BuildAsync()
    {
        // Pools keyed by lowercase style, because PhotoGallery looks them up lowercased, so a
        // capitalized style in the manifest must not silently miss.
        var pools = (await _manifestSource.LoadAsync())
            .GroupBy(photo => photo.Style.ToLowerInvariant())
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PhotoEntry>)[.. group]);

        _active = await ChooseAsync();
        var facets = await _active.FacetsAsync();
        return new Warmed(pools, facets);
    }

    // #region choose
    /// <summary>
    /// The store when it holds the whole catalogue; filled first when what is missing is small
    /// enough to write before answering; otherwise the expansion in memory, with the store filled
    /// beside the site when this site owns the fill. A store that cannot be read at all is the
    /// same as an empty one that is not filled: the site keeps serving, and the Admin tab says why.
    /// </summary>
    private async Task<IVehicleCatalogue> ChooseAsync()
    {
        if (_store is null)
        {
            return Bridge(await _seeder.LoadAsync());
        }

        int filled;
        try
        {
            filled = await _store.FilledAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _note = $"the store's catalogue could not be read ({ex.GetType().Name}), so the expansion answers from memory";
            var unread = Bridge(await _seeder.LoadAsync());
            Watch(_store);
            return unread;
        }
        Volatile.Write(ref _filled, filled);
        int missing = _store.Target - filled;
        if (missing <= 0)
        {
            return _store;
        }

        var expansion = await _seeder.LoadAsync();
        if (missing <= _store.Pace.FillAtWarmUpTo)
        {
            await FillAsync(_store, expansion, filled, pause: false);
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
            Filling = Task.Run(() => FillBesideAsync(store, filled));
        }
        else
        {
            _note = $"the store holds {filled:N0} of {_store.Target:N0}, and this site does not fill it, so the expansion answers from memory";
            Watch(_store);
        }
        return bridge;
    }

    /// <summary>
    /// For a site that does not fill this store: count it once a minute, for a day at most, and
    /// switch to it the minute it holds the whole catalogue, which is when the other site's fill
    /// finishes. Nothing when upkeep is off, which is every test host.
    /// </summary>
    private void Watch(IVehicleCatalogue store)
    {
        if (_upkeep == CatalogueUpkeep.Off)
        {
            return;
        }
        Filling = Task.Run(async () =>
        {
            for (int minute = 0; minute < 24 * 60; minute++)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                try
                {
                    int filled = await store.FilledAsync();
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
        });
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
        _counts.Clear();
        _pages.Clear();
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
    private async Task FillAsync(IVehicleCatalogue store, IReadOnlyList<Vehicle> expansion, int from, bool pause)
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
            await store.FillAsync(slice);
            Volatile.Write(ref _filled, Math.Max(Volatile.Read(ref _filled), start + slice.Count));
            if (pause && store.Pace.Pause > TimeSpan.Zero)
            {
                await Task.Delay(store.Pace.Pause);
            }
        }
    }

    /// <summary>
    /// The fill beside a running site, then the switch. Tried again a minute later when the store
    /// refuses, up to ten times.
    /// </summary>
    private async Task FillBesideAsync(IVehicleCatalogue store, int from)
    {
        IReadOnlyList<Vehicle> expansion = Interlocked.Exchange(ref _pending, null) ?? [];
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await FillAsync(store, expansion, from, pause: true);
                int filled = await store.FilledAsync();
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
                await Task.Delay(TimeSpan.FromMinutes(1));
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
    // #endregion warm
}
