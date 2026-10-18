using TheYard.Data;

namespace TheYard.Application;

// #region ports
// The three seams the layers meet at. Application declares what it needs and
// never learns where the data lives; Infrastructure implements these against
// EF Core and SQLite, against Cosmos DB, the tests against in-memory arrays,
// and the 100,000-record scale-up is a decorator over IVehicleSource that
// nothing above it can see.
//
// Every member returns a Task, because the document store has no synchronous
// driver, and a port that held a request thread while a cloud store answered
// would stall a one-vCPU container (ADR: The ports learn to wait).
/// <summary>Port: where the vehicle dataset comes from.</summary>
public interface IVehicleSource
{
    Task<IReadOnlyList<Vehicle>> LoadAsync();
}

/// <summary>Port: where the photo manifest comes from.</summary>
public interface IPhotoManifestSource
{
    Task<IReadOnlyList<PhotoEntry>> LoadAsync();
}

/// <summary>
/// Port: where everybody's bids are kept, and the one place each vehicle's
/// standing is decided. Two sites run one image over the same store, so the
/// standing (price, count, high bidder, sold) is a row or document the store
/// owns, written with a compare-and-set on its version. Memory is a cache of
/// it, and a cache can be stale; the store cannot be argued with
/// (ADR: Three readers with no memory of the project, item 5).
/// A bid belongs to a user (ADR: Accounts and per-user bids), so a buyer's row
/// is keyed by the pair and the standing by the vehicle.
/// </summary>
public interface IBidStore
{
    /// <summary>Every buyer's row, for the replay at startup.</summary>
    Task<IReadOnlyList<StoredBid>> LoadAsync();

    /// <summary>Every vehicle's stored standing, for the replay at startup.</summary>
    Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync();

    /// <summary>One vehicle's stored standing and its version, or null when the store holds none for it.</summary>
    Task<StoredStanding?> ReadStandingAsync(string vehicleId);

    /// <summary>
    /// Every buyer's row on one vehicle. Read when the store holds no standing
    /// for it, which is a vehicle bid on before standings existed or one a
    /// reset cleared, so the standing is worked out from the rows rather than
    /// taken to be the opening ask.
    /// </summary>
    Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId);

    /// <summary>
    /// Writes the buyer's row and the vehicle's new standing, but only if the
    /// standing still carries <paramref name="expectedVersion"/>; null expects
    /// there to be no standing yet. False means another writer moved it first,
    /// and nothing of this bid was kept: the caller reads again and runs the
    /// rules again.
    /// </summary>
    Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion);

    /// <summary>
    /// Forget one person's bids, and the standing of every vehicle they bid on,
    /// so the next read works each one out again from whoever is left. Not
    /// everybody's: this is what the reset button on a page a stranger can also
    /// be looking at is allowed to do (ADR: Reset is one person's start-over).
    /// </summary>
    Task ClearAsync(string userId);
}

// #endregion ports
