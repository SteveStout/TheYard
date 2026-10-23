using TheYard.Application;
using TheYard.Application.Features.Bids;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// One store's auction over hand-written fakes at its ports, for a handler test with no host (ADR: The
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

    /// <summary>One store's auction over these vehicles and nothing else.</summary>
    internal static TestAuction Over(params Vehicle[] vehicles) =>
        new(new Stock(new FakeVehicles(vehicles), new FakePhotos()), new BidBook(new MemoryBidStore()), new Room(new Random(7)));

    private sealed class FakeVehicles(Vehicle[] vehicles) : IVehicleSource
    {
        public Task<IReadOnlyList<Vehicle>> LoadAsync() => Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
    }

    private sealed class FakePhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync() => Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
}

/// <summary>
/// One store's three parts, the way a backend holds them (ADR-115). A handler test builds its handler
/// over them the way the slice's Module does; the methods named for what a visitor does read and write
/// through the same reading and writing sides, so AuctionTests reads as the rule it holds.
/// </summary>
/// <param name="Inventory">The catalogue.</param>
/// <param name="Bids">Everybody's bids.</param>
/// <param name="Room">The simulated competing bidders.</param>
internal sealed record TestAuction(Stock Inventory, BidBook Bids, Room Room)
{
    /// <summary>The vehicles as every query reads them.</summary>
    internal StandingReader Standings => new(Inventory, Bids, Room);

    internal Task<StandingVehicle?> FindAsync(string vehicleId) => Standings.FindAsync(vehicleId);

    internal Task<StandingPage> SearchAsync(VehicleFilter filter, AuctionClock clock, VehicleSort sort, int limit, int offset) =>
        Standings.SearchAsync(filter, clock, sort, limit, offset);

    internal Task<BidOutcome> PlaceBidAsync(StandingVehicle vehicle, int amount, AuctionClock clock, string userId) =>
        Bids.PlaceBidAsync(vehicle.Vehicle, amount, clock, userId);

    internal Task<BidOutcome> BuyNowAsync(StandingVehicle vehicle, AuctionClock clock, string userId) =>
        Bids.BuyNowAsync(vehicle.Vehicle, clock, userId);

    internal Task ResetAsync(string userId) => new ResetBidsHandler(Bids, Room).HandleAsync(new ResetBidsCommand(userId), CancellationToken.None);

    internal Task<IReadOnlyList<string>> RoomRoundAsync(AuctionClock clock) => RoomRoundHandler.RoundAsync(Bids, Inventory, Room, clock);

    internal IReadOnlyDictionary<string, BidView> BidsOf(string userId) => Standings.BidsOf(userId);

    internal Task<BidHistory> HistoryOfAsync(string userId) => Standings.HistoryOfAsync(userId);
}
