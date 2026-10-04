// BidService is one class in three files. This one holds the two indexes, the load from the
// store, the reads every listing makes, and Record, which every write ends in.
//   BidService.Bidding.cs   placing a bid and buying now, under the gate
//   BidService.Reset.cs     one person's start-over
// The records it hands out have a file each beside it: BidState.cs, VehicleStanding.cs and
// StoredBid.cs.
using System.Collections.Concurrent;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>
/// Everybody's bids, read from the store once at startup and written through on
/// every accepted bid. Two indexes over the same facts, because two questions
/// are asked at very different rates: what does this vehicle stand at, which is
/// asked a hundred thousand times per listing request, and what have I bid,
/// which is asked once.
/// </summary>
public sealed partial class BidService
{
    /// <summary>By vehicle. The hot path: Apply does one lookup per vehicle.</summary>
    private readonly ConcurrentDictionary<string, VehicleStanding> _standing;

    /// <summary>By user, then by vehicle. Asked once per page, not once per row.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BidState>> _byUser;

    /// <summary>Where bids are kept between restarts: read once at load, written on every accepted bid.</summary>
    private readonly IBidStore _store;

    /// <summary>Bids that live exactly as long as this process does.</summary>
    public BidService()
        : this(NullBidStore.Instance)
    {
    }

    // #region store
    /// <summary>
    /// Read once, through <see cref="LoadAsync"/>. Every read after that one is
    /// a dictionary: Apply runs over a hundred thousand vehicles on a listing
    /// request, and a per-row query would end the feature rather than persist
    /// it (ADR: The relational store).
    ///
    /// The constructor does not read the store, because a constructor cannot
    /// wait and the store has to be waited for (ADR: The ports learn to
    /// wait). The host calls LoadAsync once at startup; the writing methods
    /// call it too, so a service nobody warmed loads itself on its first bid.
    /// </summary>
    public BidService(IBidStore store)
    {
        _store = store;
        _standing = new ConcurrentDictionary<string, VehicleStanding>(StringComparer.Ordinal);
        _byUser = new ConcurrentDictionary<string, ConcurrentDictionary<string, BidState>>(StringComparer.Ordinal);
    }

    /// <summary>Held only while deciding whether a load has to start, so two first callers share one replay.</summary>
    private readonly object _loadGate = new();

    /// <summary>The replay in flight or finished, or null before anybody has asked for one.</summary>
    private Task? _loaded;

    /// <summary>
    /// Replay the store into both indexes, once, whoever asks first. A replay
    /// that failed is not kept: the next caller starts another, so a store
    /// that was down at startup is read the first time it is up rather than
    /// never (ADR: The ports learn to wait, addendum). Replaying twice is safe,
    /// because Record keeps the higher standing and the same bid twice is the
    /// same standing.
    /// </summary>
    public Task LoadAsync()
    {
        lock (_loadGate)
        {
            if (_loaded is null || _loaded.IsFaulted || _loaded.IsCanceled)
            {
                _loaded = LoadFromStoreAsync();
            }
            return _loaded;
        }
    }

    /// <summary>Every stored bid, recorded into both indexes in the order the store returns them.</summary>
    private async Task LoadFromStoreAsync()
    {
        foreach (var bid in await _store.LoadAsync())
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
    /// back to false on a vehicle that was already sold. The gate is held for
    /// the length of a dictionary read, some integer comparisons, and the
    /// store's answer.
    ///
    /// A semaphore rather than a lock, because a lock cannot be held across an
    /// await and the store is awaited inside it. Same shape, same guarantee,
    /// one bidder at a time (ADR: The ports learn to wait).
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True when nobody has a standing bid on any vehicle.</summary>
    public bool IsEmpty => _standing.IsEmpty;

    /// <summary>One user's bids, for the badges and the history.</summary>
    public IReadOnlyDictionary<string, BidState> SnapshotFor(string userId) =>
        _byUser.TryGetValue(userId, out var mine)
            ? new Dictionary<string, BidState>(mine, StringComparer.Ordinal)
            : new Dictionary<string, BidState>(StringComparer.Ordinal);

    /// <summary>Where each vehicle stands and who holds it.</summary>
    public IReadOnlyDictionary<string, VehicleStanding> Standing() =>
        new Dictionary<string, VehicleStanding>(_standing, StringComparer.Ordinal);

    // #region sold
    /// <summary>
    /// Whether anybody has bought this vehicle outright. One dictionary read,
    /// because it is asked once per vehicle on every listing answer, the way
    /// Apply is. The fact lives in the standing, and what makes it a rule is
    /// that it is asked before the next bid is taken, so a sold vehicle has no
    /// second buyer (ADR: Accounts and per-user bids, the addendum on the
    /// second buyer).
    /// </summary>
    public bool IsSold(string vehicleId) =>
        _standing.TryGetValue(vehicleId, out var held) && held.SoldBuyNow;
    // #endregion sold

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
        _standing.TryGetValue(vehicle.Id, out var held) ? vehicle.RaisedTo(held.Amount, held.BidCount) : vehicle;
    // #endregion apply

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
