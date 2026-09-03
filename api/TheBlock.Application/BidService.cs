using System.Collections.Concurrent;
using TheBlock.Data;
using TheBlock.Domain;

namespace TheBlock.Application;

/// <summary>
/// One buyer's standing on one vehicle. AtMs is when the bid was placed, which
/// the simulated room reads to decide whether enough time has passed to answer
/// it (ADR: Competing bidders).
/// </summary>
public sealed record BidState(int Amount, int BidCount, bool WonBuyNow, long AtMs);

/// <summary>
/// One vehicle's bidding, across everybody: what it stands at, how many bids
/// got it there, and who holds it. This is what the listing overlay reads and
/// what "you have been outbid" is measured against
/// (ADR: Accounts and per-user bids).
/// </summary>
public sealed record VehicleStanding(int Amount, int BidCount, string HighBidderId, bool SoldBuyNow, long AtMs);

/// <summary>One stored bid, as the store hands it back.</summary>
public sealed record StoredBid(string UserId, string VehicleId, BidState State);

/// <summary>
/// Everybody's bids, read from the store once at startup and written through on
/// every accepted bid. Two indexes over the same facts, because two questions
/// are asked at very different rates: what does this vehicle stand at, which is
/// asked a hundred thousand times per listing request, and what have I bid,
/// which is asked once.
/// </summary>
public sealed class BidService
{
    /// <summary>By vehicle. The hot path: Apply does one lookup per vehicle.</summary>
    private readonly ConcurrentDictionary<string, VehicleStanding> _standing;

    /// <summary>By user, then by vehicle. Asked once per page, not once per row.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BidState>> _byUser;

    private readonly IBidStore _store;

    /// <summary>Bids that live exactly as long as this process does.</summary>
    public BidService()
        : this(NullBidStore.Instance)
    {
    }

    // #region store
    /// <summary>
    /// Read once, here. Every read after this one is a dictionary: Apply runs
    /// over a hundred thousand vehicles on a listing request, and a per-row
    /// query would end the feature rather than persist it
    /// (ADR: The relational store).
    /// </summary>
    public BidService(IBidStore store)
    {
        _store = store;
        _standing = new ConcurrentDictionary<string, VehicleStanding>(StringComparer.Ordinal);
        _byUser = new ConcurrentDictionary<string, ConcurrentDictionary<string, BidState>>(StringComparer.Ordinal);
        foreach (var bid in store.Load())
        {
            Record(bid.UserId, bid.VehicleId, bid.State);
        }
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

    public bool IsEmpty => _standing.IsEmpty;

    /// <summary>One user's bids, for the badges and the history.</summary>
    public IReadOnlyDictionary<string, BidState> SnapshotFor(string userId) =>
        _byUser.TryGetValue(userId, out var mine)
            ? new Dictionary<string, BidState>(mine, StringComparer.Ordinal)
            : new Dictionary<string, BidState>(StringComparer.Ordinal);

    /// <summary>Where each vehicle stands and who holds it.</summary>
    public IReadOnlyDictionary<string, VehicleStanding> Standing() =>
        new Dictionary<string, VehicleStanding>(_standing, StringComparer.Ordinal);

    // #region standing-as-bids
    /// <summary>
    /// The standing, in the shape the simulated room reads (ADR-027). The room
    /// answers the price rather than the person, so it is handed everybody's
    /// high-water mark and not one account's: a room that only responded to the
    /// visitor who happened to be looking would stop being a room the moment
    /// there were two of them.
    /// </summary>
    public IReadOnlyDictionary<string, BidState> StandingAsBids()
    {
        var shaped = new Dictionary<string, BidState>(StringComparer.Ordinal);
        foreach (var (vehicleId, held) in _standing)
        {
            shaped[vehicleId] = new BidState(held.Amount, held.BidCount, held.SoldBuyNow, held.AtMs);
        }
        return shaped;
    }
    // #endregion standing-as-bids

    // #region apply
    /// <summary>
    /// What the vehicle stands at, from everybody, layered over whatever the
    /// dataset shows, and only when it is higher. The "only when higher" is not
    /// decoration: this overlay is composed with the room's (ADR-027), and a
    /// version that overwrote unconditionally would hand BidRules a stale
    /// figure, which is a minimum next bid computed against the wrong price and
    /// a bid accepted below the going rate.
    ///
    /// It takes no user on purpose. A listing shows one price to everybody, and
    /// a price that depended on who was looking would be a different auction
    /// per visitor.
    /// </summary>
    public Vehicle Apply(Vehicle vehicle) =>
        _standing.TryGetValue(vehicle.Id, out var held) && held.Amount > (vehicle.CurrentBid ?? 0)
            ? vehicle with { CurrentBid = held.Amount, BidCount = Math.Max(vehicle.BidCount, held.BidCount) }
            : vehicle;
    // #endregion apply

    // #region place
    public BidOutcome PlaceBid(Vehicle vehicle, int amount, AuctionClock clock, string userId)
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
                // The store first, then memory. The other order looks harmless
                // and is not: a store that throws would leave the dictionaries
                // holding a bid the caller was just told had failed, shown as
                // winning until the next restart deleted it. This way a failed
                // write means the bid did not happen anywhere, which is the
                // answer the caller already has.
                _store.Save(userId, vehicle.Id, state);
                Record(userId, vehicle.Id, state);
            }
            return outcome;
        }
    }
    // #endregion place

    /// <summary>Buy Now is a purchase, not a bid, so the bid count stays as-is.</summary>
    public BidOutcome BuyNow(Vehicle vehicle, AuctionClock clock, string userId)
    {
        lock (_gate)
        {
            var merged = Apply(vehicle);
            var outcome = BidRules.ResolveBuyNow(merged, clock);
            if (outcome.Kind == BidOutcomeKind.Won)
            {
                var state = new BidState(outcome.Amount, merged.BidCount, WonBuyNow: true, AtMs: clock.NowMs);
                _store.Save(userId, vehicle.Id, state);
                Record(userId, vehicle.Id, state);
            }
            return outcome;
        }
    }

    /// <summary>
    /// The demo's start-over button. It clears everybody, not just the caller,
    /// because the room's bids (ADR-027) are shared and a reset that left half
    /// of an auction standing reads as a bug however carefully it is explained.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _store.Clear();
            _standing.Clear();
            _byUser.Clear();
        }
    }

    // #region record
    /// <summary>
    /// Both indexes, from one fact. The standing only moves up: a later bid
    /// from somebody else at a lower number does not exist, because BidRules
    /// rejected it before this was called, and a replayed bid from the store
    /// arriving out of order should not be able to lower the price either.
    /// </summary>
    private void Record(string userId, string vehicleId, BidState state)
    {
        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, BidState>(StringComparer.Ordinal))[vehicleId] =
            state;

        _standing.AddOrUpdate(
            vehicleId,
            _ => new VehicleStanding(state.Amount, state.BidCount, userId, state.WonBuyNow, state.AtMs),
            (_, held) => state.Amount > held.Amount
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
                });
    }
    // #endregion record
}
