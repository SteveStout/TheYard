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
    private readonly ConcurrentDictionary<string, BidState> _bids;
    private readonly IBidStore _store;

    /// <summary>Bids that live exactly as long as this process does.</summary>
    public BidService()
        : this(NullBidStore.Instance)
    {
    }

    // #region store
    /// <summary>
    /// Bids read back from wherever the store keeps them, once, here. Every
    /// read after this one is the dictionary: Apply runs over a hundred
    /// thousand vehicles on a listing request, and a per-row query would end
    /// the feature rather than persist it (ADR: The relational store).
    /// </summary>
    public BidService(IBidStore store)
    {
        _store = store;
        _bids = new ConcurrentDictionary<string, BidState>(store.Load(), StringComparer.Ordinal);
    }
    // #endregion store

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
                var state = new BidState(
                    outcome.Amount,
                    merged.BidCount + 1,
                    WonBuyNow: outcome.Kind == BidOutcomeKind.Won,
                    AtMs: clock.NowMs);
                // The store first, then memory. The other order looks
                // harmless and is not: a store that throws would leave the
                // dictionary holding a bid the caller was just told had failed,
                // shown as winning until the next restart deleted it. This way
                // a failed write means the bid did not happen anywhere, which
                // is the answer the caller already has (the staff review,
                // 2026-09-03).
                _store.Save(vehicle.Id, state);
                _bids[vehicle.Id] = state;
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
                var state = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true, AtMs: clock.NowMs);
                _store.Save(vehicle.Id, state);
                _bids[vehicle.Id] = state;
            }
            return outcome;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _store.Clear();
            _bids.Clear();
        }
    }
}
