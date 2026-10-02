// The two ways a buyer moves a price: placing a bid and buying now. Both take the gate, run the
// rules from BidRules against everybody's standing, and write the store before memory. They have
// a file of their own because this is where a bid is accepted or refused, and that decision
// reads best without the indexes and the reads around it (the type itself is in BidService.cs).
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>The writes that move a price: a bid, and a buy-now purchase, one bidder at a time.</summary>
public sealed partial class BidService
{
    // #region place
    /// <summary>
    /// Places one bid for one buyer. The rules run against the vehicle as
    /// everybody's bids leave it, under the gate, and an accepted bid is
    /// written to the store before it is recorded in memory.
    /// </summary>
    public async Task<BidOutcome> PlaceBidAsync(Vehicle vehicle, int amount, AuctionClock clock, string userId)
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            var merged = Apply(vehicle);
            // Sold is read under the same gate as the write that makes it true,
            // so two buyers cannot both find it false.
            var outcome = BidRules.ResolveBid(merged, amount, clock, IsSold(vehicle.Id));
            if (outcome.Kind != BidOutcomeKind.Rejected)
            {
                var state = new BidState(
                    outcome.Amount,
                    merged.BidCount + 1,
                    WonBuyNow: outcome.Kind == BidOutcomeKind.Won,
                    AtMs: clock.NowMs);
                // The store first, then memory. The other order looks harmless
                // and is not: a store that throws would leave the dictionaries
                // holding a bid the caller was just told had failed, shown as
                // winning until the next restart deleted it. This way a failed
                // write means the bid did not happen anywhere, which is the
                // answer the caller already has.
                await _store.SaveAsync(userId, vehicle.Id, state);
                Record(userId, vehicle.Id, state);
            }
            return outcome;
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
    /// with the same sentence a bid gets.
    /// </summary>
    public async Task<BidOutcome> BuyNowAsync(Vehicle vehicle, AuctionClock clock, string userId)
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            var merged = Apply(vehicle);
            var outcome = BidRules.ResolveBuyNow(merged, clock, IsSold(vehicle.Id));
            if (outcome.Kind == BidOutcomeKind.Won)
            {
                var state = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true, AtMs: clock.NowMs);
                await _store.SaveAsync(userId, vehicle.Id, state);
                Record(userId, vehicle.Id, state);
            }
            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }
}
