using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// An auction over hand-written fakes at its ports, for a handler test with no host (ADR: The
/// onion cut into vertical slices, rule 7): the catalogue is the vehicles given, there are no
/// photos, the bids are kept in memory and the room is the real one.
/// </summary>
internal static class TestAuctions
{
    /// <summary>Noon on the test clock, when <see cref="Live"/>'s vehicles are live.</summary>
    internal static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4));

    /// <summary>The test clock at noon.</summary>
    internal static AuctionClock AtNoon() => TestData.ClockAt(Noon);

    /// <summary>A vehicle whose auction is live at noon, with an optional buy-now price.</summary>
    internal static Vehicle Live(string prefix, int currentBid = 22_800, int? buyNow = null)
    {
        var clock = AtNoon();
        string id = Enumerable.Range(0, 500).Select(i => $"{prefix}-{i}").First(candidate => AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live);
        return TestData.Vehicle(id: id, currentBid: currentBid) with { BuyNowPrice = buyNow };
    }

    /// <summary>An auction over these vehicles and nothing else.</summary>
    internal static Auction Over(params Vehicle[] vehicles) =>
        new(new InventoryService(new FakeVehicles(vehicles), new FakePhotos()), new BidService(new MemoryBidStore()), new MarketService(new Random(7)));

    private sealed class FakeVehicles(Vehicle[] vehicles) : IVehicleSource
    {
        public Task<IReadOnlyList<Vehicle>> LoadAsync() => Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
    }

    private sealed class FakePhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync() => Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
}
