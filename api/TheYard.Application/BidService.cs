using System.Collections.Concurrent;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

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

    // #region reset
    /// <summary>
    /// One person's start-over.
    ///
    /// <para>This used to clear everybody, and the comment explaining why was
    /// honest about it: the room's bids are shared, and a reset that took away
    /// your bid while leaving the room's counter-bid standing reads as a bug
    /// however carefully it is explained. That was written when a bid belonged
    /// to a browser. Since bids got owners and a database, it meant any signed
    /// in visitor could delete every other visitor's rows, on a site whose own
    /// changelog says two visitors can outbid each other and both be told the
    /// truth (ADR: Reset is one person's start-over).</para>
    ///
    /// <para>The original reasoning survives, narrowed to the vehicles the
    /// caller actually touched: their bids go, the room's answers on those same
    /// vehicles go with them, and each of those vehicles gets its standing
    /// recomputed from whoever is left rather than deleted, so a stranger who
    /// bid on the same car keeps their bid and keeps the lead they earned.</para>
    ///
    /// <para>Returns the vehicles that have nobody bidding on them any more,
    /// which the caller passes to the room. Not every vehicle the caller
    /// touched: the first version of this returned all of them, and "vehicles
    /// this person bid on" is not "vehicles only this person bid on", so
    /// clearing the room's answer on a shared car took away a stranger's outbid
    /// badge and dropped the price a stranger was competing at. The room is a
    /// separate service and this one does not reach into it.</para>
    /// </summary>
    public IReadOnlyList<string> Reset(string userId)
    {
        lock (_gate)
        {
            if (!_byUser.TryRemove(userId, out var mine))
            {
                return [];
            }

            string[] touched = mine.Keys.ToArray();
            var orphaned = new List<string>();
            _store.Clear(userId);

            foreach (string vehicleId in touched)
            {
                // Recomputed, not removed. Removing it would hand the vehicle
                // back to its opening ask and quietly delete a third person's
                // bid, which is the same defect one size smaller.
                var best = _byUser
                    .Select(user => user.Value.TryGetValue(vehicleId, out var state) ? (user.Key, state) : (null, null))
                    .Where(pair => pair.Item2 is not null)
                    .OrderByDescending(pair => pair.Item2!.Amount)
                    .ThenBy(pair => pair.Item2!.AtMs)
                    .FirstOrDefault();

                if (best.Item1 is null)
                {
                    // Nobody left on this one, so the room's answer to it has
                    // nothing to be an answer to. These are the only vehicles
                    // the caller gets to clear the room on.
                    _standing.TryRemove(vehicleId, out _);
                    orphaned.Add(vehicleId);
                    continue;
                }

                var state = best.Item2!;
                _standing[vehicleId] = new VehicleStanding(
                    state.Amount, state.BidCount, best.Item1, state.WonBuyNow, state.AtMs);
            }

            return orphaned;
        }
    }
    // #endregion reset

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
