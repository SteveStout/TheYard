using TheYard.Data;

namespace TheYard.Application;

// #region ports
// The three seams the layers meet at. Application declares what it needs and
// never learns where the data lives; Infrastructure implements these against
// EF Core and SQLite, the tests against in-memory arrays, and the 100,000-record
// scale-up is a decorator over IVehicleSource that nothing above it can see.
/// <summary>Port: where the vehicle dataset comes from.</summary>
public interface IVehicleSource
{
    IReadOnlyList<Vehicle> Load();
}

/// <summary>Port: where the photo manifest comes from.</summary>
public interface IPhotoManifestSource
{
    IReadOnlyList<PhotoEntry> Load();
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
    IReadOnlyList<StoredBid> Load();

    void Save(string userId, string vehicleId, BidState state);

    /// <summary>
    /// Forget one person's bids. Not everybody's: this is what the reset button
    /// on a page a stranger can also be looking at is allowed to do
    /// (ADR: Reset is one person's start-over).
    /// </summary>
    void Clear(string userId);
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

    public IReadOnlyList<StoredBid> Load() => [];

    public void Save(string userId, string vehicleId, BidState state)
    {
    }

    public void Clear(string userId)
    {
    }
}
// #endregion ports
