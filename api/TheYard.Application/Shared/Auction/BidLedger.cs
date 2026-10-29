// The bids as every listing reads them: the two indexes, the load from the store, the reads, and Record,
// which every write ends in. Its own type, apart from BidBook's writes, because it changes when what a
// listing reads changes, never when the rules of placing a bid do. The fold it replays with is
// StandingFold.cs; the records it hands out have a file each beside it: BidState.cs, VehicleStanding.cs
// and StoredBid.cs.
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
public sealed class BidLedger : IBidReader
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
    /// wait). The host calls LoadAsync once at startup; BidBook's writing methods
    /// call it too, so a book nobody warmed loads itself on its first bid.
    /// </summary>
    /// <param name="store">Where the bids are kept.</param>
    public BidLedger(IBidStore store)
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
    ///
    /// <para>One replay is shared, so cancelling has to mean two different
    /// things and does: the caller who STARTS the replay lends it their token,
    /// and every caller who arrives while it is in flight stops only their own
    /// wait. A visitor who closed a tab therefore never abandons the load that
    /// every other request is queued behind; they get their thread back and the
    /// replay finishes for the callers still there. In a running container the
    /// starter is the host at startup, so the token the store sees is the
    /// host's stopping token; a replay that is cancelled is not kept, and the
    /// next caller starts a fresh one, which is the same self-healing rule a
    /// failed replay already had (ADR: The ports learn to wait, the addendum on
    /// the token).</para>
    /// </summary>
    /// <param name="cancellation">Lent to the store when this caller starts the replay; otherwise it ends this caller's wait alone.</param>
    public Task LoadAsync(CancellationToken cancellation)
    {
        Task loading;
        bool started = false;
        lock (_loadGate)
        {
            if (_loaded is null || _loaded.IsFaulted || _loaded.IsCanceled)
            {
                _loaded = LoadFromStoreAsync(cancellation);
                started = true;
            }
            loading = _loaded;
        }
        // The starter already owns the token the replay is running under, so waiting on it
        // again through WaitAsync would only wrap the same cancellation twice.
        return started || loading.IsCompleted ? loading : loading.WaitAsync(cancellation);
    }

    /// <summary>
    /// The store's standings first, then every buyer's row. A vehicle with rows
    /// and no standing is one bid on before standings were kept, or one a reset
    /// cleared, so its standing is worked out from the rows. A standing whose
    /// high bidder has no row is the document store's two writes interrupted
    /// between them; the standing is the authority, so the holder's row is put
    /// back in memory from it rather than the price being lowered to match.
    /// </summary>
    /// <param name="cancellation">The starting caller's, which in a running container is the host's stopping token.</param>
    private async Task LoadFromStoreAsync(CancellationToken cancellation) =>
        Replay(await _store.LoadStandingsAsync(cancellation), await _store.LoadAsync(cancellation));

    /// <summary>
    /// Throws the cache away and reads it again from the store. The other site's
    /// bids reach this process's listing here, at startup, and on the next write
    /// to the same vehicle; the rules never wait for it, because they read the
    /// store themselves.
    /// </summary>
    /// <remarks>Called by <see cref="BidBook.RefreshAsync"/> under the book's gate, so no write lands half way through.</remarks>
    /// <param name="cancellation">The caller's cancellation, carried to the store: this read is one caller's and nobody shares it.</param>
    internal async Task RefreshAsync(CancellationToken cancellation)
    {
        var standings = await _store.LoadStandingsAsync(cancellation);
        var bids = await _store.LoadAsync(cancellation);
        _standing.Clear();
        _byUser.Clear();
        Replay(standings, bids);
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
            if (!_standing.ContainsKey(group.Key) && StandingFold.Derive(group) is { } derived)
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
    internal void Record(string userId, string vehicleId, BidState state, VehicleStanding standing)
    {
        Remember(userId, vehicleId, state);
        _standing[vehicleId] = standing;
    }

    /// <summary>One buyer's row into the by-user index, replacing whatever that buyer held on that vehicle.</summary>
    internal void Remember(string userId, string vehicleId, BidState state) =>
        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, BidState>(StringComparer.Ordinal))[vehicleId] =
            state;
    // #endregion record

    /// <summary>Sets one vehicle's cached standing to what the store said, or forgets it when the store has none.</summary>
    /// <param name="vehicleId">The vehicle.</param>
    /// <param name="held">The store's standing, or null.</param>
    internal void Hold(string vehicleId, VehicleStanding? held)
    {
        if (held is null)
        {
            _standing.TryRemove(vehicleId, out _);
        }
        else
        {
            _standing[vehicleId] = held;
        }
    }

    /// <summary>The vehicles one buyer holds a row on.</summary>
    /// <param name="userId">The buyer.</param>
    internal string[] TouchedBy(string userId) => _byUser.TryGetValue(userId, out var mine) ? mine.Keys.ToArray() : [];

    /// <summary>Forgets every row one buyer held.</summary>
    /// <param name="userId">The buyer.</param>
    internal void Forget(string userId) => _byUser.TryRemove(userId, out _);
}
