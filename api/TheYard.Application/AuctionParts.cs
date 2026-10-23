// The three parts one store's auction is made of, each split into what reads it and what writes it
// (ADR-115: A request is a query or a command). A query handler is handed the reading side only,
// so a read can never place a bid or move the room; a command handler takes the writing side
// beside it. Stock, BidBook and Room implement them, one instance of each per store, held by
// the store's backend.
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

// #region auction-parts
/// <summary>The catalogue as a query reads it: one vehicle, several, the first live ones, the filters' values and a page.</summary>
public interface IStockReader
{
    /// <summary>One vehicle by id, at the dataset's figures, or null when the catalogue has no such id.</summary>
    Task<Vehicle?> FindAsync(string id);

    /// <summary>The vehicles with these ids that the catalogue holds, in no promised order.</summary>
    Task<IReadOnlyList<Vehicle>> FindManyAsync(IReadOnlyCollection<string> ids);

    /// <summary>The first live auctions in the catalogue's own order, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<Vehicle>> FirstLiveAsync(AuctionClock clock, int take);

    /// <summary>The values behind the catalogue's filters.</summary>
    Task<InventoryFacets> FacetsAsync();

    /// <summary>One page of the catalogue, filtered and sorted, with the overlay laid on before either.</summary>
    Task<SearchResult> SearchAsync(VehicleFilter filter, AuctionClock clock, VehicleSort sort = VehicleSort.EndingSoonest, int limit = int.MaxValue, int offset = 0, ListingOverlay? overlay = null);
}

/// <summary>Everybody's bids as a query reads them: the standing on each vehicle, one buyer's bids, and whether a vehicle is sold.</summary>
public interface IBidReader
{
    /// <summary>Whether nobody has bid on anything, so a cold listing pays for no overlay.</summary>
    bool IsEmpty { get; }

    /// <summary>One buyer's own bids, by vehicle id.</summary>
    IReadOnlyDictionary<string, BidState> SnapshotFor(string userId);

    /// <summary>Every vehicle's standing, by id.</summary>
    IReadOnlyDictionary<string, VehicleStanding> Standing();

    /// <summary>Whether anybody has bought this vehicle outright.</summary>
    bool IsSold(string vehicleId);

    /// <summary>The standing in the shape the room reads it, one bid per vehicle.</summary>
    IReadOnlyDictionary<string, BidState> StandingAsBids();

    /// <summary>The vehicle with everybody's bids laid on its dataset figures.</summary>
    Vehicle Apply(Vehicle vehicle);
}

/// <summary>The writes that move everybody's bids: a bid, a buy-now purchase and one buyer's start-over.</summary>
public interface IBidWriter
{
    /// <summary>One bid from one buyer, measured against the vehicle as it stands.</summary>
    Task<BidOutcome> PlaceBidAsync(Vehicle vehicle, int amount, AuctionClock clock, string userId);

    /// <summary>A buy-now purchase from one buyer, at the vehicle's buy-now price.</summary>
    Task<BidOutcome> BuyNowAsync(Vehicle vehicle, AuctionClock clock, string userId);

    /// <summary>One buyer's bids removed; answers the vehicles nobody else is bidding on any more.</summary>
    Task<IReadOnlyList<string>> ResetAsync(string userId);
}

/// <summary>The room's bids as a query reads them.</summary>
public interface IRoomReader
{
    /// <summary>Whether the room has bid on nothing yet.</summary>
    bool IsEmpty { get; }

    /// <summary>The room's standing bid on every vehicle it has bid on, by id.</summary>
    IReadOnlyDictionary<string, MarketBid> Snapshot();

    /// <summary>The vehicle with the room's bid laid on it.</summary>
    Vehicle Apply(Vehicle vehicle);
}

/// <summary>The writes that move the room: one round of bidding, and forgetting the vehicles a reset left alone.</summary>
public interface IRoomWriter
{
    /// <summary>One round over these candidates against everybody's standing; answers the ids it raised.</summary>
    IReadOnlyList<string> Tick(IReadOnlyList<Vehicle> candidates, IReadOnlyDictionary<string, BidState> buyerBids, AuctionClock clock, int maxBids = 3);

    /// <summary>The room's answers on these vehicles dropped.</summary>
    void Forget(IEnumerable<string> vehicleIds);
}
// #endregion auction-parts
