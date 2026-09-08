using TheYard.Data;

namespace TheYard.Application;

// #region ports
// The three seams the layers meet at. Application declares what it needs and
// never learns where the data lives; Infrastructure implements these against
// EF Core and SQLite, against Cosmos DB, the tests against in-memory arrays,
// and the 100,000-record scale-up is a decorator over IVehicleSource that
// nothing above it can see.
//
// Every member returns a Task. The first store was a file and the ports were
// synchronous because reading a file is; the second store was a database with
// a synchronous driver; the third has no synchronous driver at all, and a port
// that blocks a request thread while a cloud store answers is a performance
// defect on a one-vCPU container (ADR: The ports learn to wait).
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
/// Port: where everybody's bids are kept between one run of this process and
/// the next. Read once at startup and written through on every accepted bid,
/// which is the shape the bidding path can afford (ADR: The relational store).
/// A bid belongs to a user (ADR: Accounts and per-user bids), so the key is the
/// pair and not the vehicle.
/// </summary>
public interface IBidStore
{
    Task<IReadOnlyList<StoredBid>> LoadAsync();

    Task SaveAsync(string userId, string vehicleId, BidState state);

    /// <summary>
    /// Forget one person's bids. Not everybody's: this is what the reset button
    /// on a page a stranger can also be looking at is allowed to do
    /// (ADR: Reset is one person's start-over).
    /// </summary>
    Task ClearAsync(string userId);
}

/// <summary>
/// The port wired to nothing. Bidding works without a store and forgets at the
/// end of the process, which is what the unit tests want and what this
/// application did before it had anywhere to write.
/// </summary>
public sealed class NullBidStore : IBidStore
{
    public static readonly NullBidStore Instance = new();

    private NullBidStore()
    {
    }

    public Task<IReadOnlyList<StoredBid>> LoadAsync() => Task.FromResult<IReadOnlyList<StoredBid>>([]);

    public Task SaveAsync(string userId, string vehicleId, BidState state) => Task.CompletedTask;

    public Task ClearAsync(string userId) => Task.CompletedTask;
}
// #endregion ports
