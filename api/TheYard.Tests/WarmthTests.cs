using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Application;
using TheYard.Data;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The pipeline awaits a store's catalogue before an endpoint reads it, so a
/// request that reaches a store still loading waits asynchronously rather than
/// blocking a request thread on the load (ADR: Three readers with no memory of
/// the project). These tests hold the awaiting itself: the task the pipeline
/// gets back is incomplete for exactly as long as the source is, and finished
/// at no cost once the store is warm.
/// </summary>
public class WarmthTests
{
    // #region warm-before-reading
    [Fact]
    public async Task A_request_on_a_cold_store_waits_for_the_catalogue_without_blocking()
    {
        var source = new GatedVehicles();
        var backend = ColdBackend(source);

        var waiting = Warmth.EnsureAsync(backend);

        // Still loading: the task is pending and the store says so. A version
        // that blocked would never have returned this task at all.
        Assert.False(waiting.IsCompleted);
        Assert.False(backend.Inventory.IsWarm);

        source.Release();
        await waiting;

        Assert.True(backend.Inventory.IsWarm);
        Assert.Empty(backend.Inventory.GetAll());
    }

    [Fact]
    public async Task A_warm_store_is_waited_on_for_no_time_at_all()
    {
        var source = new GatedVehicles();
        var backend = ColdBackend(source);
        source.Release();
        await backend.Inventory.WarmAsync();

        var again = Warmth.EnsureAsync(backend);

        Assert.True(again.IsCompletedSuccessfully);
        Assert.Equal(1, source.Loads);
    }
    // #endregion warm-before-reading

    // #region let-go
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task A_catalogue_nobody_has_asked_for_in_a_while_is_let_go_and_the_next_request_loads_it_again()
    {
        var source = new GatedVehicles();
        var backend = ColdBackend(source);
        source.Release();
        await Warmth.EnsureAsync(backend, Noon);

        Assert.False(backend.Inventory.ReleaseIfIdle(TenMinutes, Noon.AddMinutes(9)), "nine minutes is not ten");
        Assert.True(backend.Inventory.ReleaseIfIdle(TenMinutes, Noon.AddMinutes(10)));
        Assert.False(backend.Inventory.IsWarm);
        Assert.False(backend.Inventory.ReleaseIfIdle(TenMinutes, Noon.AddMinutes(30)), "there is nothing left to let go");

        // The next request finds a cold store and awaits the load, the way a
        // cold store's first request always has.
        await Warmth.EnsureAsync(backend, Noon.AddMinutes(31));

        Assert.True(backend.Inventory.IsWarm);
        Assert.Equal(2, source.Loads);
    }

    [Fact]
    public async Task A_request_touches_before_it_reads_so_a_catalogue_in_use_is_never_the_one_let_go()
    {
        var source = new GatedVehicles();
        var backend = ColdBackend(source);
        source.Release();
        await Warmth.EnsureAsync(backend, Noon);

        // Somebody asks at 12:09, so at 12:10 it has been idle for a minute.
        await Warmth.EnsureAsync(backend, Noon.AddMinutes(9));

        Assert.False(backend.Inventory.ReleaseIfIdle(TenMinutes, Noon.AddMinutes(10)));
        Assert.True(backend.Inventory.IsWarm);
        Assert.Equal(1, source.Loads);
    }

    [Fact]
    public void A_load_in_flight_is_never_dropped()
    {
        var source = new GatedVehicles();
        var backend = ColdBackend(source);
        var waiting = Warmth.EnsureAsync(backend, Noon);

        // Somebody is waiting on this load. However long ago it was touched,
        // letting it go would start a second one under them.
        Assert.False(backend.Inventory.ReleaseIfIdle(TenMinutes, Noon.AddHours(1)));
        Assert.False(waiting.IsCompleted);
        Assert.Equal(1, source.Loads);
    }

    [Fact]
    public async Task The_keeper_lets_go_of_the_store_a_site_does_not_serve_and_never_of_the_one_it_does()
    {
        var sql = ColdBackend(Loaded(), "sql");
        var cosmos = ColdBackend(Loaded(), "cosmos");
        await Warmth.EnsureAsync(sql, Noon);
        await Warmth.EnsureAsync(cosmos, Noon);
        var keeper = new CatalogueKeeper(new Backends([sql, cosmos], "sql"), TenMinutes, NullLogger<CatalogueKeeper>.Instance);

        Assert.Empty(keeper.Sweep(Noon.AddMinutes(5)));
        string[] letGo = ["cosmos"];
        Assert.Equal(letGo, keeper.Sweep(Noon.AddHours(1)));

        Assert.True(sql.Inventory.IsWarm, "the default store is what the site is, however quiet the hour");
        Assert.False(cosmos.Inventory.IsWarm);
    }

    [Fact]
    public async Task With_no_idle_time_configured_nothing_is_ever_let_go()
    {
        var sql = ColdBackend(Loaded(), "sql");
        var cosmos = ColdBackend(Loaded(), "cosmos");
        await Warmth.EnsureAsync(cosmos, Noon);
        var keeper = new CatalogueKeeper(new Backends([sql, cosmos], "sql"), TimeSpan.Zero, NullLogger<CatalogueKeeper>.Instance);

        Assert.Empty(keeper.Sweep(Noon.AddDays(1)));
        Assert.True(cosmos.Inventory.IsWarm);
    }

    private static GatedVehicles Loaded()
    {
        var source = new GatedVehicles();
        source.Release();
        return source;
    }
    // #endregion let-go

    private static Backend ColdBackend(IVehicleSource source, string key = "sql") => new()
    {
        Key = key,
        Name = "SQLite",
        Database = new DatabaseState(true, "SQLite"),
        Startup = new StartupTimings(),
        Inventory = new InventoryService(source, new NoPhotos()),
        Bids = new BidService(),
        Market = new MarketService(),
        Probe = () => Task.FromResult(true),
        UserStore = _ => null,
    };

    /// <summary>A catalogue that loads when the test says so, and counts how often it was asked.</summary>
    private sealed class GatedVehicles : IVehicleSource
    {
        private readonly TaskCompletionSource<IReadOnlyList<Vehicle>> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Loads { get; private set; }

        public Task<IReadOnlyList<Vehicle>> LoadAsync()
        {
            Loads++;
            return _gate.Task;
        }

        public void Release() => _gate.TrySetResult([]);
    }

    private sealed class NoPhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync() =>
            Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
}
