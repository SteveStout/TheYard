// BidBook is everybody's bids: the writes, under one gate, over the ledger every listing reads.
//   BidLedger.cs          the two indexes, the load from the store and the reads (IBidReader)
//   BidBook.Bidding.cs    placing a bid and buying now, under the gate
//   BidBook.Standing.cs   reading the store's standing under the gate
//   BidBook.Reset.cs      one person's start-over
//   StandingFold.cs       how accepted bids add up to a standing
using TheYard.Data;

namespace TheYard.Application.Shared.Auction;

/// <summary>
/// Everybody's bids. The store decides each vehicle's standing; the ledger keeps
/// a cache of it for the listing, and this writes through the store and records
/// what the store accepted. A bid is never decided against the cache: the bid path
/// reads the store's standing under the gate and writes with a compare-and-set,
/// so two sites over one store cannot both sell a vehicle.
/// </summary>
/// <param name="store">Where bids are kept between restarts.</param>
public sealed partial class BidBook(IBidStore store) : IBidReader, IBidWriter
{
    /// <summary>Where bids are kept between restarts: read once at load, written on every accepted bid.</summary>
    private readonly IBidStore _store = store;

    /// <summary>The indexes every listing reads.</summary>
    private readonly BidLedger _ledger = new(store);

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

    /// <summary>Loads the ledger from the store once; see <see cref="BidLedger.LoadAsync"/>.</summary>
    public Task LoadAsync() => _ledger.LoadAsync();

    /// <summary>
    /// Throws the cache away and reads it again from the store, under the gate so no write lands half way
    /// through. The other site's bids reach this process's listing here, at startup, and on the next write to
    /// the same vehicle; the rules never wait for it, because they read the store themselves.
    /// </summary>
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _ledger.RefreshAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public bool IsEmpty => _ledger.IsEmpty;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, BidState> SnapshotFor(string userId) => _ledger.SnapshotFor(userId);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, VehicleStanding> Standing() => _ledger.Standing();

    /// <inheritdoc/>
    public bool IsSold(string vehicleId) => _ledger.IsSold(vehicleId);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, BidState> StandingAsBids() => _ledger.StandingAsBids();

    /// <inheritdoc/>
    public Vehicle Apply(Vehicle vehicle) => _ledger.Apply(vehicle);
}
