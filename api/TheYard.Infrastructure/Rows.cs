namespace TheYard.Infrastructure;

/// <summary>
/// A vehicle as the database holds it. Deliberately not <c>TheYard.Data.Vehicle</c>:
/// the domain record is sealed, has init-only members and exposes its lists as
/// <see cref="IReadOnlyList{T}"/>, all of which are right for a value passed
/// around the application and wrong for something a mapper has to build a row
/// at a time. Keeping them separate is what lets the domain record stay the
/// shape the domain wants (ADR: The relational store).
/// </summary>
public sealed class VehicleRow
{
    /// <summary>Where this vehicle sat in the seed file. See YardDbContext for why it is stored.</summary>
    public required int Seq { get; set; }

    public required string Id { get; set; }

    public required string Vin { get; set; }

    public required int Year { get; set; }

    public required string Make { get; set; }

    public required string Model { get; set; }

    public required string Trim { get; set; }

    public required string BodyStyle { get; set; }

    public required string ExteriorColor { get; set; }

    public required string InteriorColor { get; set; }

    public required string Engine { get; set; }

    public required string Transmission { get; set; }

    public required string Drivetrain { get; set; }

    public required int OdometerKm { get; set; }

    public required string FuelType { get; set; }

    public required double ConditionGrade { get; set; }

    public required string ConditionReport { get; set; }

    /// <summary>A primitive collection: EF stores it as a JSON column on SQLite.</summary>
    public required List<string> DamageNotes { get; set; }

    public required string TitleStatus { get; set; }

    public required string Province { get; set; }

    public required string City { get; set; }

    public required string AuctionStart { get; set; }

    public required int StartingBid { get; set; }

    public required int? ReservePrice { get; set; }

    public required int? BuyNowPrice { get; set; }

    public required List<string> Images { get; set; }

    public required string SellingDealership { get; set; }

    public required string Lot { get; set; }

    public required int? CurrentBid { get; set; }

    public required int BidCount { get; set; }
}

/// <summary>One row of the vendored photo manifest.</summary>
public sealed class PhotoRow
{
    public required int Seq { get; set; }

    public required string File { get; set; }

    public required string Style { get; set; }

    public required string Title { get; set; }
}

/// <summary>
/// One buyer's standing on one vehicle, and the only thing in this database
/// that changes after startup. One row per buyer per vehicle: a later bid from
/// the same person replaces their earlier one rather than appending, because
/// what the application asks is "where do I stand", never "what did I bid an
/// hour ago" (ADR: Accounts and per-user bids).
/// </summary>
public sealed class BidRow
{
    /// <summary>The account that placed it. Half of this row's key.</summary>
    public required string UserId { get; set; }

    public required string VehicleId { get; set; }

    public required int Amount { get; set; }

    public required int BidCount { get; set; }

    public required bool WonBuyNow { get; set; }

    public required long AtMs { get; set; }

    /// <summary>
    /// The optimistic concurrency token (ADR: The SQL Server backend). On SQL
    /// Server this is a <c>rowversion</c> the database maintains; on SQLite the
    /// store assigns a new value on every save, because SQLite has no such type.
    /// It guards this one buyer's row, so the race it catches is the same
    /// account writing from two sites at once. Two different buyers are two
    /// rows and never meet here; what they race for is the vehicle's standing,
    /// which <see cref="StandingRow"/> guards with a version of its own.
    /// Nullable because a row that has not been read yet does not have one.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// One vehicle's standing across everybody: the price, the count, the high
/// bidder and whether it has sold. The store owns it so two sites over one
/// database agree on it; a write names the version it read and is refused if
/// another writer moved it first (ADR: Three readers with no memory of the
/// project, item 5).
/// </summary>
public sealed class StandingRow
{
    /// <summary>The vehicle, and the key: one standing per vehicle.</summary>
    public required string VehicleId { get; set; }

    public required int Amount { get; set; }

    public required int BidCount { get; set; }

    /// <summary>The account holding the highest bid.</summary>
    public required string HighBidderId { get; set; }

    public required bool SoldBuyNow { get; set; }

    public required long AtMs { get; set; }

    /// <summary>
    /// Moves by one on every write. A plain number rather than rowversion, so
    /// the compare-and-set reads the same on SQL Server and SQLite: an update
    /// that names the version it read and changes no row lost the race.
    /// </summary>
    public required long Version { get; set; }
}
