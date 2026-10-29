using TheYard.Data;

namespace TheYard.Application.Shared.Auction;

// #region ports
// The three seams the layers meet at. Application declares what it needs and
// never learns where the data lives; Infrastructure implements these against
// EF Core and SQLite, against Cosmos DB, the tests against in-memory arrays,
// and the 100,000-record scale-up is a decorator over IVehicleSource that
// nothing above it can see.
//
// Every member returns a Task, because the document store has no synchronous
// driver, and a port that held a request thread while a cloud store answered
// would stall a one-vCPU container (ADR: The ports learn to wait). Every member
// also takes a CancellationToken, because a read the visitor walked away from is
// work the site pays for twice, and a path that drops the token cannot be made to
// stop later without changing every caller (the same record, the addendum on the
// token). Taking it is not the same as cancelling: a bid write that has entered
// the gate runs to its end, and the two startup loads take the host's stopping
// token rather than any request's.
/// <summary>Port: where the vehicle dataset comes from.</summary>
public interface IVehicleSource
{
    Task<IReadOnlyList<Vehicle>> LoadAsync(CancellationToken cancellation);
}

/// <summary>Port: where the photo manifest comes from.</summary>
public interface IPhotoManifestSource
{
    Task<IReadOnlyList<PhotoEntry>> LoadAsync(CancellationToken cancellation);
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
    Task<IReadOnlyList<StoredBid>> LoadAsync(CancellationToken cancellation);

    /// <summary>Every vehicle's stored standing, for the replay at startup.</summary>
    Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync(CancellationToken cancellation);

    /// <summary>One vehicle's stored standing and its version, or null when the store holds none for it.</summary>
    Task<StoredStanding?> ReadStandingAsync(string vehicleId, CancellationToken cancellation);

    /// <summary>
    /// Every buyer's row on one vehicle. Read when the store holds no standing
    /// for it, which is a vehicle bid on before standings existed or one a
    /// reset cleared, so the standing is worked out from the rows rather than
    /// taken to be the opening ask.
    /// </summary>
    Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId, CancellationToken cancellation);

    /// <summary>
    /// Writes the buyer's row and the vehicle's new standing, but only if the
    /// standing still carries <paramref name="expectedVersion"/>; null expects
    /// there to be no standing yet. False means another writer moved it first,
    /// and nothing of this bid was kept: the caller reads again and runs the
    /// rules again.
    /// </summary>
    Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion, CancellationToken cancellation);

    /// <summary>
    /// Forget one person's bids, and the standing of every vehicle they bid on,
    /// so the next read works each one out again from whoever is left. Not
    /// everybody's: this is what the reset button on a page a stranger can also
    /// be looking at is allowed to do (ADR: Reset is one person's start-over).
    /// </summary>
    Task ClearAsync(string userId, CancellationToken cancellation);
}

/// <summary>
/// Port: the catalogue as the store holds it, all hundred thousand vehicles with
/// the slot each id's hash gives it, and the store doing the filtering, the
/// ordering and the paging (ADR: The store does the work). Application plans a
/// listing out of these primitives, the three schedule segments, the room's and
/// the bids' overlay, the page, and the adapters only translate a filter and an
/// order into their own query: EF Core over the Catalogue table, the SDK over the
/// catalogue container. Every read is bounded by the page it asks for; nothing
/// here hands back the whole catalogue.
/// </summary>
public interface IVehicleCatalogue
{
    /// <summary>How many vehicles this catalogue is the first of: the expansion's target count.</summary>
    int Target { get; }

    /// <summary>How the store wants to be filled: a batch at a time, a pause between, and up to how many it fills at warm-up.</summary>
    CatalogueFillPace Pace { get; }

    /// <summary>How many of the first <see cref="Target"/> vehicles the store holds with their slot, read as one count.</summary>
    Task<int> FilledAsync(CancellationToken cancellation);

    /// <summary>Writes one batch of the expansion, each vehicle at its place; a vehicle already there is left as it is or rewritten the same.</summary>
    Task FillAsync(IReadOnlyList<CatalogueEntry> batch, CancellationToken cancellation);

    /// <summary>How many vehicles match, split by the three schedule segments, in one read where the store allows it.</summary>
    Task<SegmentCounts> CountAsync(CatalogueFilter filter, CancellationToken cancellation);

    /// <summary>One page of the matches in one order, skipped and taken in the store's own query.</summary>
    Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take, CancellationToken cancellation);

    /// <summary>The vehicles with these ids, by point read where the address names the partition; an id the catalogue lacks is simply absent.</summary>
    Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids, CancellationToken cancellation);

    /// <summary>The distinct values behind the filters, read once at warm-up.</summary>
    Task<InventoryFacets> FacetsAsync(CancellationToken cancellation);
}

// #endregion ports
