namespace TheBlock.Infrastructure;

/// <summary>
/// A vehicle as the database holds it. Deliberately not <c>TheBlock.Data.Vehicle</c>:
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
/// The buyer's standing on one vehicle, and the only thing in this database
/// that changes after startup. One row per vehicle: a later bid replaces the
/// earlier one rather than appending, because the application has never needed
/// bid history and a table that keeps it would be a claim that it does.
/// </summary>
public sealed class BidRow
{
    public required string VehicleId { get; set; }

    public required int Amount { get; set; }

    public required int BidCount { get; set; }

    public required bool WonBuyNow { get; set; }

    public required long AtMs { get; set; }
}
