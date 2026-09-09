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

    private static Backend ColdBackend(IVehicleSource source) => new()
    {
        Key = "sql",
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
