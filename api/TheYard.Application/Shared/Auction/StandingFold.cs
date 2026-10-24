// How accepted bids add up to a vehicle's standing: the one fold the bid path, the load and the replay all use.
// Its own type because it is a rule with no state: it changes when the rule does, never when the book's caching
// or its writes do.
namespace TheYard.Application.Shared.Auction;

/// <summary>The standing one accepted bid leaves, and the standing a set of buyer rows adds up to.</summary>
public static class StandingFold
{
    // #region next
    /// <summary>
    /// The standing one accepted bid leaves. The price only moves up: a bid at
    /// or under the standing price that got this far is a buy-now at a price
    /// the room had already passed, and it marks the vehicle sold without
    /// taking the lead from whoever holds it. The count never drops, and sold
    /// never comes undone.
    /// </summary>
    public static VehicleStanding Next(VehicleStanding? held, string userId, BidState state) =>
        held is null
            ? new VehicleStanding(state.Amount, state.BidCount, userId, state.WonBuyNow, state.AtMs)
            : state.Amount > held.Amount
                ? new VehicleStanding(
                    state.Amount,
                    Math.Max(held.BidCount, state.BidCount),
                    userId,
                    held.SoldBuyNow || state.WonBuyNow,
                    state.AtMs)
                : held with
                {
                    BidCount = Math.Max(held.BidCount, state.BidCount),
                    SoldBuyNow = held.SoldBuyNow || state.WonBuyNow,
                };

    /// <summary>
    /// The standing a set of buyer rows adds up to, oldest first through
    /// <see cref="Next"/>, so a tie on price goes to whoever got there first.
    /// Null when there are no rows at all.
    /// </summary>
    public static VehicleStanding? Derive(IEnumerable<StoredBid> bids)
    {
        VehicleStanding? held = null;
        foreach (var bid in bids.OrderBy(bid => bid.State.AtMs).ThenBy(bid => bid.UserId, StringComparer.Ordinal))
        {
            held = Next(held, bid.UserId, bid.State);
        }
        return held;
    }
    // #endregion next
}
