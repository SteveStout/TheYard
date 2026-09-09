using TheYard.Api;
using TheYard.Application;
using TheYard.Data;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>A backend with nothing behind it, for tests about how backends are chosen and compared rather than what they hold.</summary>
internal static class FakeBackend
{
    public static Backend Named(string key, string name, bool ready = true) => new()
    {
        Key = key,
        Name = name,
        Database = new DatabaseState(ready, name),
        Startup = new StartupTimings(),
        Inventory = new InventoryService(new EmptyVehicles(), new EmptyPhotos()),
        Bids = new BidService(),
        Market = new MarketService(),
        Probe = () => Task.FromResult(ready),
        UserStore = _ => null,
    };

    private sealed class EmptyVehicles : IVehicleSource
    {
        public Task<IReadOnlyList<Vehicle>> LoadAsync() =>
            Task.FromResult<IReadOnlyList<Vehicle>>([]);
    }

    private sealed class EmptyPhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync() =>
            Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
}
