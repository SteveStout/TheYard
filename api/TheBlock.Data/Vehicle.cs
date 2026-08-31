namespace TheBlock.Data;

/// <summary>
/// A vehicle listing exactly as it appears in data/vehicles.json. Property
/// names map to the payload's snake_case via JsonNamingPolicy.SnakeCaseLower,
/// so the wire shape stays identical to the source dataset.
///
/// Sealed, like every data record here: record equality is value-based but
/// includes a runtime-type check that inheritance would quietly break, and a
/// wire contract is not an extension point. Tests compare whole vehicle
/// lists by value, and `with` copies power the bid overlay — both rely on
/// equality staying purely "same fields".
/// </summary>
public sealed record Vehicle
{
    public required string Id { get; init; }
    public required string Vin { get; init; }
    public required int Year { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required string Trim { get; init; }
    public required string BodyStyle { get; init; }
    public required string ExteriorColor { get; init; }
    public required string InteriorColor { get; init; }
    public required string Engine { get; init; }
    public required string Transmission { get; init; }
    public required string Drivetrain { get; init; }
    public required int OdometerKm { get; init; }
    public required string FuelType { get; init; }
    public required double ConditionGrade { get; init; }
    public required string ConditionReport { get; init; }
    public required IReadOnlyList<string> DamageNotes { get; init; }
    public required string TitleStatus { get; init; }
    public required string Province { get; init; }
    public required string City { get; init; }
    public required string AuctionStart { get; init; }
    public required int StartingBid { get; init; }

    /// <summary>null → no reserve.</summary>
    public required int? ReservePrice { get; init; }

    /// <summary>null → no Buy Now option.</summary>
    public required int? BuyNowPrice { get; init; }

    public required IReadOnlyList<string> Images { get; init; }
    public required string SellingDealership { get; init; }
    public required string Lot { get; init; }

    /// <summary>null until the first bid is placed (bid_count == 0).</summary>
    public required int? CurrentBid { get; init; }

    public required int BidCount { get; init; }
}

