// A vehicle as every query reads it: the dataset's figures, then everybody's bids, then the room's,
// with its sold flag beside it, and one buyer's bids with everybody else's answer folded in. It is
// the reading half of the auction's three parts (ADR-115: A request is a query or a command): it is
// handed the three parts' reading sides only, so nothing that reads through it can
// place a bid or move the room, and the price a bid is measured against still has one copy.
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>
/// One store's vehicles at the price they stand at, and one buyer's badges, read for the queries and
/// for the commands that answer with what they left.
/// </summary>
/// <param name="inventory">The store's catalogue, read side.</param>
/// <param name="bids">Everybody's bids on that catalogue, read side.</param>
/// <param name="room">The simulated competing bidders on that catalogue, read side.</param>
public sealed class StandingReader(IStockReader inventory, IBidReader bids, IRoomReader room)
{
    // #region overlays
    /// <summary>
    /// The overlay a listing applies to every row: everybody's bids, then the room's. Each one only
    /// raises the price (StandingRules.RaisedTo), so the page shows the highest of the dataset, the
    /// bids and the room, and a competitor who has gone higher is what the buyer sees. It names the
    /// vehicles it moves, because the store holds the dataset's figures and a listing that filters or
    /// sorts by price has to work those vehicles out from the moved ones (ADR: The store does the
    /// work). Null when nobody has bid at all, so a cold listing pays for neither. IsEmpty rather
    /// than a snapshot count, because a snapshot copies the whole dictionary just to ask whether it
    /// is empty.
    /// </summary>
    private ListingOverlay? Overlay()
    {
        if (bids.IsEmpty && room.IsEmpty)
        {
            return null;
        }
        var moved = new HashSet<string>(StringComparer.Ordinal);
        if (!bids.IsEmpty)
        {
            moved.UnionWith(bids.Standing().Keys);
        }
        if (!room.IsEmpty)
        {
            moved.UnionWith(room.Snapshot().Keys);
        }
        return new ListingOverlay(moved, AsItStands);
    }

    /// <summary>The vehicle at the price it stands at: the dataset, then everybody's bids, then the room's.</summary>
    private Vehicle AsItStands(Vehicle vehicle) => room.Apply(bids.Apply(vehicle));
    // #endregion overlays

    /// <summary>One vehicle by id, standing at its price and with its sold flag, or null when the catalogue has no such id.</summary>
    public async Task<StandingVehicle?> FindAsync(string vehicleId) =>
        await inventory.FindAsync(vehicleId) is { } vehicle ? new StandingVehicle(AsItStands(vehicle), bids.IsSold(vehicleId)) : null;

    /// <summary>One page of the catalogue, filtered and sorted, each vehicle at the price it stands at and with its sold flag.</summary>
    public async Task<StandingPage> SearchAsync(VehicleFilter filter, AuctionClock clock, VehicleSort sort, int limit, int offset)
    {
        var page = await inventory.SearchAsync(filter, clock, sort, limit, offset, Overlay());
        return new StandingPage(page.Total, [.. page.Vehicles.Select(vehicle => new StandingVehicle(vehicle, bids.IsSold(vehicle.Id)))]);
    }

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
        var against = room.Snapshot();
        var views = new Dictionary<string, BidView>(StringComparer.Ordinal);
        foreach (var (id, bid) in mine)
        {
            standing.TryGetValue(id, out var held);
            against.TryGetValue(id, out var answered);

            int highest = Math.Max(held?.Amount ?? bid.Amount, answered?.Amount ?? 0);
            bool someoneElseHolds = held is not null && held.Amount > bid.Amount;
            bool roomHolds = answered is not null && answered.Amount > bid.Amount;
            bool outbid = !bid.WonBuyNow && (someoneElseHolds || roomHolds);

            views[id] = new BidView(
                bid.Amount,
                // The count everybody has added to, not one buyer's, so the count never falls
                // when a buyer retakes a lead somebody else raised.
                Math.Max(Math.Max(bid.BidCount, held?.BidCount ?? 0), answered?.BidCount ?? 0),
                bid.WonBuyNow,
                bid.AtMs,
                outbid,
                answered?.Amount,
                highest);
        }
        return views;
    }

    /// <summary>
    /// One buyer's history for the account page, newest first: each vehicle's title beside the
    /// buyer's standing on it, or "(withdrawn)" when the vehicle has left the catalogue.
    /// </summary>
    public async Task<BidHistory> HistoryOfAsync(string userId)
    {
        var mine = BidsOf(userId);
        var titles = (await inventory.FindManyAsync(mine.Keys.ToList()))
            .ToDictionary(v => v.Id, v => $"{v.Year} {v.Make} {v.Model}", StringComparer.Ordinal);
        var entries = mine
            .OrderByDescending(entry => entry.Value.AtMs)
            .Select(entry => new BidHistoryEntry(
                entry.Key,
                titles.TryGetValue(entry.Key, out var title) ? title : "(withdrawn)",
                entry.Value))
            .ToList();
        return new BidHistory(entries.Count, entries);
    }
    // #endregion views
}
