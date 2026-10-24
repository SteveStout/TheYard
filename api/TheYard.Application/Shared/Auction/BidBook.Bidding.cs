// The two ways a buyer moves a price: placing a bid and buying now. Both take the gate, run the
// rules from BidRules against the store's standing, and write it back with a compare-and-set. They have
// a file of their own because this is where a bid is accepted or refused, and that decision
// reads best without the indexes and the reads around it (the type itself is in BidBook.cs).
using TheYard.Data;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>The writes that move a price: a bid, and a buy-now purchase, one bidder at a time.</summary>
public sealed partial class BidBook
{
    // #region place
    /// <summary>
    /// Places one bid for one buyer. The rules run against the store's standing
    /// for the vehicle, read under the gate, and the bid is kept only if the
    /// store still holds that same standing when it is written. If the other
    /// site moved it in between, the standing is read again and the rules run
    /// again, so a bid that no longer clears the minimum is refused with the
    /// sentence it would have got a moment later.
    /// </summary>
    public async Task<BidOutcome> PlaceBidAsync(Vehicle vehicle, int amount, AuctionClock clock, string userId)
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                var (held, version) = await CurrentAsync(vehicle.Id);
                var merged = held is null ? vehicle : vehicle.RaisedTo(held.Amount, held.BidCount);
                // Sold is read from the same standing the write will compare
                // against, so two buyers, on one site or two, cannot both find
                // it false.
                var outcome = BidRules.ResolveBid(merged, amount, clock, held?.SoldBuyNow ?? false);
                if (outcome.Kind == BidOutcomeKind.Rejected)
                {
                    return outcome;
                }

                var state = new BidState(
                    outcome.Amount,
                    merged.BidCount + 1,
                    WonBuyNow: outcome.Kind == BidOutcomeKind.Won,
                    AtMs: clock.NowMs);
                var next = StandingFold.Next(held, userId, state);
                // The store first, then memory. A store that throws leaves the
                // dictionaries as they were, so a failed write means the bid did
                // not happen anywhere, which is the answer the caller already has.
                if (await _store.TrySaveAsync(userId, vehicle.Id, state, next, version))
                {
                    _ledger.Record(userId, vehicle.Id, state, next);
                    return outcome;
                }
            }
            return BidOutcome.Rejected(MovedReason);
        }
        finally
        {
            _gate.Release();
        }
    }
    // #endregion place

    /// <summary>
    /// Buy Now is a purchase, not a bid, so the bid count stays as-is. It is
    /// also the end of the auction for everybody: the second buyer is refused
    /// with the same sentence a bid gets, whichever site they bought on.
    /// </summary>
    public async Task<BidOutcome> BuyNowAsync(Vehicle vehicle, AuctionClock clock, string userId)
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                var (held, version) = await CurrentAsync(vehicle.Id);
                var merged = held is null ? vehicle : vehicle.RaisedTo(held.Amount, held.BidCount);
                var outcome = BidRules.ResolveBuyNow(merged, clock, held?.SoldBuyNow ?? false);
                if (outcome.Kind != BidOutcomeKind.Won)
                {
                    return outcome;
                }

                var state = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true, AtMs: clock.NowMs);
                var next = StandingFold.Next(held, userId, state);
                if (await _store.TrySaveAsync(userId, vehicle.Id, state, next, version))
                {
                    _ledger.Record(userId, vehicle.Id, state, next);
                    return outcome;
                }
            }
            return BidOutcome.Rejected(MovedReason);
        }
        finally
        {
            _gate.Release();
        }
    }
}
