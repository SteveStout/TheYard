// BidBook is one class in four files. This one holds the two indexes, the load from the
// store, the reads every listing makes, and Record, which every write ends in.
//   BidBook.Bidding.cs   placing a bid and buying now, under the gate
//   BidBook.Standing.cs  reading the store's standing and working out the next one
//   BidBook.Reset.cs     one person's start-over
// The records it hands out have a file each beside it: BidState.cs, VehicleStanding.cs and
// StoredBid.cs.
using System.Collections.Concurrent;
using TheYard.Data;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>
/// Everybody's bids. The store decides each vehicle's standing; this class keeps
/// a cache of it for the listing, read from the store at startup and refreshed
/// on every write. Two indexes over the same facts, because two questions are
/// asked at very different rates: what does this vehicle stand at, which is
/// asked a hundred thousand times per listing request, and what have I bid,
/// which is asked once. A bid is never decided against the cache: the bid path
/// reads the store's standing under the gate and writes with a compare-and-set,
/// so two sites over one store cannot both sell a vehicle.
/// </summary>
public sealed partial class BidBook : IBidReader, IBidWriter
{
    /// <summary>By vehicle, a cache of the store's standing. The hot path: Apply does one lookup per vehicle.</summary>
    private readonly ConcurrentDictionary<string, VehicleStanding> _standing;

    /// <summary>By user, then by vehicle. Asked once per page, not once per row.</summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BidState>> _byUser;

    /// <summary>Where bids are kept between restarts: read once at load, written on every accepted bid.</summary>
    private readonly IBidStore _store;

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
    public BidBook(IBidStore store)
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
    /// because it writes the store's facts over themselves.
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

    /// <summary>
    /// The store's standings first, then every buyer's row. A vehicle with rows
    /// and no standing is one bid on before standings were kept, or one a reset
    /// cleared, so its standing is worked out from the rows. A standing whose
    /// high bidder has no row is the document store's two writes interrupted
    /// between them; the standing is the authority, so the holder's row is put
    /// back in memory from it rather than the price being lowered to match.
    /// </summary>
    private async Task LoadFromStoreAsync() =>
        Replay(await _store.LoadStandingsAsync(), await _store.LoadAsync());

    /// <summary>
    /// Throws the cache away and reads it again from the store. The other site's
    /// bids reach this process's listing here, at startup, and on the next write
    /// to the same vehicle; the rules never wait for it, because they read the
    /// store themselves.
    /// </summary>
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var standings = await _store.LoadStandingsAsync();
            var bids = await _store.LoadAsync();
            _standing.Clear();
            _byUser.Clear();
            Replay(standings, bids);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// What the store holds, into both indexes. Two repairs on the way: a
    /// vehicle with rows and no standing gets one worked out from its rows, and
    /// a standing whose holder has no row gets the holder's row back from the
    /// standing, because the standing is the authority.
    /// </summary>
    private void Replay(IReadOnlyList<StoredStanding> standings, IReadOnlyList<StoredBid> bids)
    {
        foreach (var stored in standings)
        {
            _standing[stored.VehicleId] = stored.Standing;
        }

        foreach (var group in bids.GroupBy(bid => bid.VehicleId, StringComparer.Ordinal))
        {
            foreach (var bid in group)
            {
                Remember(bid.UserId, bid.VehicleId, bid.State);
            }
            if (!_standing.ContainsKey(group.Key) && Derive(group) is { } derived)
            {
                _standing[group.Key] = derived;
            }
        }

        foreach (var stored in standings)
        {
            var held = stored.Standing;
            if (!_byUser.TryGetValue(held.HighBidderId, out var mine) || !mine.ContainsKey(stored.VehicleId))
            {
                Remember(held.HighBidderId, stored.VehicleId, new BidState(held.Amount, held.BidCount, held.SoldBuyNow, held.AtMs));
            }
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
    /// Both indexes, from one write the store accepted: the buyer's row and the
    /// standing it decided. The standing is the store's answer, so it is set
    /// rather than merged; there is nothing in memory that could know better.
    /// </summary>
    private void Record(string userId, string vehicleId, BidState state, VehicleStanding standing)
    {
        Remember(userId, vehicleId, state);
        _standing[vehicleId] = standing;
    }

    /// <summary>One buyer's row into the by-user index, replacing whatever that buyer held on that vehicle.</summary>
    private void Remember(string userId, string vehicleId, BidState state) =>
        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, BidState>(StringComparer.Ordinal))[vehicleId] =
            state;
    // #endregion record
}
