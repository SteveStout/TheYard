using System.Collections.Concurrent;
using TheBlock.Data;
using TheBlock.Domain;

namespace TheBlock.Application;

/// <summary>
/// The buyer's standing on one vehicle. AtMs is when the bid was placed, which
/// the simulated room reads to decide whether enough time has passed to answer
/// it (ADR: Competing bidders).
/// </summary>
public sealed record BidState(int Amount, int BidCount, bool WonBuyNow, long AtMs);

/// <summary>
/// The single anonymous buyer's bids, held in API memory (this is an
/// isolated demo; a real system would persist per-user bids). The overlay
/// is applied to vehicles BEFORE filtering and sorting, so price filters
/// see the same figures the UI displays.
/// </summary>
public sealed class BidService
{
    private readonly ConcurrentDictionary<string, BidState> _bids = new();

    /// <summary>
    /// Bidding is read, decide, write. A ConcurrentDictionary makes each of
    /// those three atomic and the sequence of them not, which is the shape of
    /// a lost update: two posts on the same vehicle both read $23,300, both
    /// pass the rules, and the lower one lands second. Worse across the two
    /// methods, where an ordinary bid landing after a buy-now flips WonBuyNow
    /// back to false on a vehicle that was already sold. The lock is held for
    /// the length of a dictionary read and some integer comparisons.
    /// </summary>
    private readonly object _gate = new();

    public bool IsEmpty => _bids.IsEmpty;

    public IReadOnlyDictionary<string, BidState> Snapshot() =>
        new Dictionary<string, BidState>(_bids);

    // #region apply
    /// <summary>
    /// The buyer's bid layered over whatever the vehicle already shows, and
    /// only when it is higher. The "only when higher" is not decoration: this
    /// overlay is composed with the room's (ADR-027), and a version that
    /// overwrote unconditionally would hand BidRules the buyer's own stale
    /// figure, which is a minimum next bid computed against the wrong price
    /// and a bid accepted below the going rate.
    /// </summary>
    public Vehicle Apply(Vehicle vehicle) =>
        _bids.TryGetValue(vehicle.Id, out var bid) && bid.Amount > (vehicle.CurrentBid ?? 0)
            ? vehicle with { CurrentBid = bid.Amount, BidCount = Math.Max(vehicle.BidCount, bid.BidCount) }
            : vehicle;
    // #endregion apply

    public BidOutcome PlaceBid(Vehicle vehicle, int amount, AuctionClock clock)
    {
        lock (_gate)
        {
            var merged = Apply(vehicle);
            var outcome = BidRules.ResolveBid(merged, amount, clock);
            if (outcome.Kind != BidOutcomeKind.Rejected)
            {
                _bids[vehicle.Id] = new BidState(
                    outcome.Amount,
                    merged.BidCount + 1,
                    WonBuyNow: outcome.Kind == BidOutcomeKind.Won,
                    AtMs: clock.NowMs);
            }
            return outcome;
        }
    }

    /// <summary>Buy Now is a purchase, not a bid, so the bid count stays as-is.</summary>
    public BidOutcome BuyNow(Vehicle vehicle, AuctionClock clock)
    {
        lock (_gate)
        {
            var merged = Apply(vehicle);
            var outcome = BidRules.ResolveBuyNow(merged, clock);
            if (outcome.Kind == BidOutcomeKind.Won)
            {
                _bids[vehicle.Id] = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true, AtMs: clock.NowMs);
            }
            return outcome;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _bids.Clear();
        }
    }
}
