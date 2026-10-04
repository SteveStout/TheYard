// One store's auction as a visitor meets it: the catalogue, everybody's bids and the simulated
// room, composed here and nowhere else. Endpoints ask this class and never compose the three
// services themselves. A vehicle leaves it already standing (the highest of the dataset, the bids
// and the room) with its sold flag beside it, so no handler can show a price without the room or
// a bought vehicle as open, and the price a bid is measured against has one copy.
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>
/// The use cases a visitor drives on one store: list and read vehicles at the price they stand at,
/// place a bid, buy now, start over, move the room, and read their own badges.
/// </summary>
/// <param name="inventory">The store's catalogue.</param>
/// <param name="bids">Everybody's bids on that catalogue.</param>
/// <param name="market">The simulated competing bidders on that catalogue.</param>
public sealed class Auction(InventoryService inventory, BidService bids, MarketService market)
{
    /// <summary>How many live auctions the room looks at in one round, beside the ones somebody is bidding on.</summary>
    private const int RoomRoundLiveCount = 40;

    // #region overlays
    /// <summary>
    /// The overlay a listing applies to every row: everybody's bids, then the room's. Each one only
    /// raises the price (StandingRules.RaisedTo), so the page shows the highest of the dataset, the
    /// bids and the room, and a competitor who has gone higher is what the buyer sees. Null when
    /// nobody has bid at all, so a cold listing pays for neither. IsEmpty rather than a snapshot
    /// count, because a snapshot copies the whole dictionary just to ask whether it is empty.
    /// </summary>
    private Func<Vehicle, Vehicle>? Overlay() => (bids.IsEmpty, market.IsEmpty) switch
    {
        (true, true) => null,
        (false, true) => bids.Apply,
        (true, false) => market.Apply,
        _ => AsItStands,
    };

    /// <summary>The vehicle at the price it stands at: the dataset, then everybody's bids, then the room's.</summary>
    private Vehicle AsItStands(Vehicle vehicle) => market.Apply(bids.Apply(vehicle));
    // #endregion overlays

    /// <summary>One vehicle by id, standing at its price and with its sold flag, or null when the catalogue has no such id.</summary>
    public StandingVehicle? Find(string vehicleId) =>
        inventory.GetById(vehicleId) is { } vehicle ? new StandingVehicle(AsItStands(vehicle), bids.IsSold(vehicleId)) : null;

    /// <summary>Whether anybody has bought this vehicle outright, which closes it to everybody.</summary>
    public bool IsSold(string vehicleId) => bids.IsSold(vehicleId);

    /// <summary>The values behind the catalogue's filters: every make, body style, province and the rest.</summary>
    public InventoryFacets Facets() => inventory.Facets();

    /// <summary>One page of the catalogue, filtered and sorted, each vehicle at the price it stands at and with its sold flag.</summary>
    public StandingPage Search(VehicleFilter filter, AuctionClock clock, VehicleSort sort, int limit, int offset)
    {
        var page = inventory.Search(filter, clock, sort, limit, offset, Overlay());
        return new StandingPage(page.Total, [.. page.Vehicles.Select(vehicle => new StandingVehicle(vehicle, bids.IsSold(vehicle.Id)))]);
    }

    // #region bidding
    /// <summary>
    /// One bid from one buyer, measured against the room's standing price as well as everybody's
    /// bids. Measuring against the dataset's figure alone would let a buyer retake the lead with a
    /// bid below the going rate.
    /// </summary>
    public Task<BidOutcome> PlaceBidAsync(StandingVehicle vehicle, int amount, AuctionClock clock, string userId) =>
        bids.PlaceBidAsync(vehicle.Vehicle, amount, clock, userId);

    /// <summary>A buy-now purchase from one buyer, at the vehicle's buy-now price, against the same standing.</summary>
    public Task<BidOutcome> BuyNowAsync(StandingVehicle vehicle, AuctionClock clock, string userId) =>
        bids.BuyNowAsync(vehicle.Vehicle, clock, userId);

    /// <summary>
    /// One buyer's start-over: their bids go, and so do the room's answers on the vehicles nobody
    /// else is bidding on any more. Leaving the room's counter-bid standing would make the reset
    /// clear one side of an auction and not the other.
    /// </summary>
    public async Task ResetAsync(string userId) => market.Forget(await bids.ResetAsync(userId));
    // #endregion bidding

    // #region room-round
    /// <summary>
    /// One round of bidding by the room. The room answers a price rather than a person, so it is
    /// handed everybody's standing, not one account's. Its candidates are every vehicle somebody
    /// is bidding on plus the first live auctions in the catalogue, so the grid moves even for a
    /// visitor who has bid on nothing. The first ones rather than a search, because a search
    /// derives a status for every row and sorts the matches to keep a handful of them.
    /// </summary>
    /// <returns>The ids the room raised.</returns>
    public IReadOnlyList<string> RoomRound(AuctionClock clock)
    {
        var standing = bids.StandingAsBids();
        var contested = standing.Keys
            .Select(inventory.GetById)
            .OfType<Vehicle>();
        var live = inventory.GetAll()
            .Where(v => AuctionSchedule.StatusFor(v.Id, clock) == AuctionStatus.Live)
            .Take(RoomRoundLiveCount);
        var candidates = contested.Concat(live).DistinctBy(v => v.Id).ToList();
        return market.Tick(candidates, standing, clock);
    }
    // #endregion room-round

    // #region views
    /// <summary>
    /// One buyer's bids, each with everybody else's answer folded in. A vehicle bought outright is
    /// never outbid, whatever anyone does afterwards: the sale already happened.
    /// <para>The buyer's own bids are read first and everybody else's second. The reads are not
    /// atomic, so a bid placed between them shows in one and not the other; reading the others
    /// second means the stale answer is "you have been outbid" rather than "you are winning", and
    /// only the second of those can cost somebody an auction.</para>
    /// </summary>
    public IReadOnlyDictionary<string, BidView> BidsOf(string userId)
    {
        var mine = bids.SnapshotFor(userId);
        var standing = bids.Standing();
        var room = market.Snapshot();
        var views = new Dictionary<string, BidView>(StringComparer.Ordinal);
        foreach (var (id, bid) in mine)
        {
            standing.TryGetValue(id, out var held);
            room.TryGetValue(id, out var against);

            int highest = Math.Max(held?.Amount ?? bid.Amount, against?.Amount ?? 0);
            bool someoneElseHolds = held is not null && held.Amount > bid.Amount;
            bool roomHolds = against is not null && against.Amount > bid.Amount;
            bool outbid = !bid.WonBuyNow && (someoneElseHolds || roomHolds);

            views[id] = new BidView(
                bid.Amount,
                // The count everybody has added to, not one buyer's, so the count never falls
                // when a buyer retakes a lead somebody else raised.
                Math.Max(Math.Max(bid.BidCount, held?.BidCount ?? 0), against?.BidCount ?? 0),
                bid.WonBuyNow,
                bid.AtMs,
                outbid,
                against?.Amount,
                highest);
        }
        return views;
    }

    /// <summary>
    /// One buyer's history for the account page, newest first: each vehicle's title beside the
    /// buyer's standing on it, or "(withdrawn)" when the vehicle has left the catalogue.
    /// </summary>
    public BidHistory HistoryOf(string userId)
    {
        var entries = BidsOf(userId)
            .OrderByDescending(entry => entry.Value.AtMs)
            .Select(entry => new BidHistoryEntry(
                entry.Key,
                inventory.GetById(entry.Key) is { } v ? $"{v.Year} {v.Make} {v.Model}" : "(withdrawn)",
                entry.Value))
            .ToList();
        return new BidHistory(entries.Count, entries);
    }
    // #endregion views
}
