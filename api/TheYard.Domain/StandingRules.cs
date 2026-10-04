// What a vehicle stands at once bids are layered over the dataset: the one rule for raising its
// shown price, and whether the seller's reserve is met at that price. The buyer's bids, the
// room's bids and the room's price to beat all raise a price through RaisedTo, so the rule has
// one copy and the three callers cannot drift apart.
using TheYard.Data;

namespace TheYard.Domain;

/// <summary>Whether the standing bid has reached the seller's reserve.</summary>
public enum ReserveStatus
{
    /// <summary>The vehicle has no reserve, so it sells at any price.</summary>
    NoReserve,

    /// <summary>The standing bid is at or above the reserve.</summary>
    Met,

    /// <summary>The standing bid is below the reserve, or nobody has bid yet.</summary>
    NotMet,
}

/// <summary>The rules for the price a vehicle shows once bids are layered over the dataset.</summary>
public static class StandingRules
{
    // #region raised-to
    extension(Vehicle vehicle)
    {
        /// <summary>
        /// The vehicle with a bid layered over it, and only when the bid is higher than the
        /// price it already shows. A lower bid leaves the vehicle as it was, so laying the
        /// buyer's bids and then the room's over the dataset always ends on the highest of the
        /// three. The bid count keeps the larger of the two counts, because both count the same
        /// auction from different sides.
        /// </summary>
        public Vehicle RaisedTo(int amount, int bidCount) =>
            amount > (vehicle.CurrentBid ?? 0)
                ? vehicle with { CurrentBid = amount, BidCount = Math.Max(vehicle.BidCount, bidCount) }
                : vehicle;

        /// <summary>Whether the reserve is met at the price this vehicle shows.</summary>
        public ReserveStatus Reserve => ReserveOf(vehicle.ReservePrice, vehicle.CurrentBid);
    }
    // #endregion raised-to

    // #region reserve
    /// <summary>
    /// Whether a reserve is met by a standing bid. No reserve means the vehicle sells at any
    /// price. A reserve is met once the standing bid reaches it exactly or goes past it, and
    /// with no bid yet it cannot be met. The amount itself never leaves the server; only this
    /// answer does.
    /// </summary>
    private static ReserveStatus ReserveOf(int? reservePrice, int? currentBid)
    {
        if (reservePrice is not { } reserve)
        {
            return ReserveStatus.NoReserve;
        }
        return currentBid is { } bid && bid >= reserve ? ReserveStatus.Met : ReserveStatus.NotMet;
    }
    // #endregion reserve
}
