using System.Collections.Concurrent;
using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Application;

/// <summary>The buyer's standing on one vehicle.</summary>
public sealed record BidState(int Amount, int BidCount, bool WonBuyNow);

/// <summary>
/// The single anonymous buyer's bids, held in API memory (this is an
/// isolated demo — a real system would persist per-user bids). The overlay
/// is applied to vehicles BEFORE filtering and sorting, so price filters
/// see the same figures the UI displays.
/// </summary>
public sealed class BidService
{
    private readonly ConcurrentDictionary<string, BidState> _bids = new();

    public IReadOnlyDictionary<string, BidState> Snapshot() =>
        new Dictionary<string, BidState>(_bids);

    /// <summary>The vehicle with the buyer's bid layered over the dataset's figures.</summary>
    public Vehicle Apply(Vehicle vehicle) =>
        _bids.TryGetValue(vehicle.Id, out var bid)
            ? vehicle with { CurrentBid = bid.Amount, BidCount = bid.BidCount }
            : vehicle;

    public BidOutcome PlaceBid(Vehicle vehicle, int amount, AuctionClock clock)
    {
        var merged = Apply(vehicle);
        var outcome = BidRules.ResolveBid(merged, amount, clock);
        if (outcome.Kind != BidOutcomeKind.Rejected)
        {
            _bids[vehicle.Id] = new BidState(
                outcome.Amount,
                merged.BidCount + 1,
                WonBuyNow: outcome.Kind == BidOutcomeKind.Won);
        }
        return outcome;
    }

    /// <summary>Buy Now is a purchase, not a bid — the bid count stays as-is.</summary>
    public BidOutcome BuyNow(Vehicle vehicle, AuctionClock clock)
    {
        var merged = Apply(vehicle);
        var outcome = BidRules.ResolveBuyNow(merged, clock);
        if (outcome.Kind == BidOutcomeKind.Won)
        {
            _bids[vehicle.Id] = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true);
        }
        return outcome;
    }

    public void Reset() => _bids.Clear();
}
