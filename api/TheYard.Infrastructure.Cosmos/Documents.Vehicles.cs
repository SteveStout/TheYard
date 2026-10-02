// The document shape of the vehicles container: one seed vehicle per document, partitioned on
// the make. The experiment's catalogue containers hold the same shape. One file per container,
// so what a container holds is one short file to open; the mapping to the domain record is in
// Documents.cs.
using System.Text.Json.Serialization;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
/// <summary>
/// One seed vehicle. Partitioned on <c>make</c>. It does not carry the dataset's
/// <c>images</c>: every body style in the seed has a photo pool, so
/// <c>PhotoGallery.SelectPhotos</c> replaces those URLs for every vehicle and
/// they were 30 per cent of every document for nothing (ADR: A second store on
/// Cosmos DB, and what it costs).
/// </summary>
public sealed class VehicleDocument
{
    /// <summary>The vehicle id from the dataset, which is also the document id (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The vehicle's place in the seed file, so a read can put the catalogue back in file order (<c>seq</c>).</summary>
    public int Seq { get; set; }

    /// <summary>The vehicle identification number (<c>vin</c>).</summary>
    public string Vin { get; set; } = "";

    /// <summary>The model year (<c>year</c>).</summary>
    public int Year { get; set; }

    /// <summary>The manufacturer, and the partition key (<c>make</c>).</summary>
    public string Make { get; set; } = "";

    /// <summary>The model name (<c>model</c>).</summary>
    public string Model { get; set; } = "";

    /// <summary>The trim level (<c>trim</c>).</summary>
    public string Trim { get; set; } = "";

    /// <summary>The body style, which picks the photo pool (<c>body_style</c>).</summary>
    public string BodyStyle { get; set; } = "";

    /// <summary>The paint color (<c>exterior_color</c>).</summary>
    public string ExteriorColor { get; set; } = "";

    /// <summary>The cabin color (<c>interior_color</c>).</summary>
    public string InteriorColor { get; set; } = "";

    /// <summary>The engine, as the dataset describes it (<c>engine</c>).</summary>
    public string Engine { get; set; } = "";

    /// <summary>The transmission (<c>transmission</c>).</summary>
    public string Transmission { get; set; } = "";

    /// <summary>Which wheels are driven (<c>drivetrain</c>).</summary>
    public string Drivetrain { get; set; } = "";

    /// <summary>The odometer reading, in kilometers (<c>odometer_km</c>).</summary>
    public int OdometerKm { get; set; }

    /// <summary>The fuel the vehicle runs on (<c>fuel_type</c>).</summary>
    public string FuelType { get; set; } = "";

    /// <summary>The inspection grade (<c>condition_grade</c>).</summary>
    public double ConditionGrade { get; set; }

    /// <summary>The inspector's written report (<c>condition_report</c>).</summary>
    public string ConditionReport { get; set; } = "";

    /// <summary>Each piece of damage the inspection noted (<c>damage_notes</c>).</summary>
    public List<string> DamageNotes { get; set; } = [];

    /// <summary>The title's status, such as clean or salvage (<c>title_status</c>).</summary>
    public string TitleStatus { get; set; } = "";

    /// <summary>The province the vehicle is in (<c>province</c>).</summary>
    public string Province { get; set; } = "";

    /// <summary>The city the vehicle is in (<c>city</c>).</summary>
    public string City { get; set; } = "";

    /// <summary>The dataset's auction start date string, passed through as the dataset has it (<c>auction_start</c>).</summary>
    public string AuctionStart { get; set; } = "";

    /// <summary>The opening bid, in whole dollars (<c>starting_bid</c>).</summary>
    public int StartingBid { get; set; }

    /// <summary>The reserve, in whole dollars, or null when there is none (<c>reserve_price</c>).</summary>
    public int? ReservePrice { get; set; }

    /// <summary>The buy-now price, in whole dollars, or null when there is none (<c>buy_now_price</c>).</summary>
    public int? BuyNowPrice { get; set; }

    /// <summary>The dealership selling the vehicle (<c>selling_dealership</c>).</summary>
    public string SellingDealership { get; set; } = "";

    /// <summary>The lot number (<c>lot</c>).</summary>
    public string Lot { get; set; } = "";

    /// <summary>The dataset's current bid, in whole dollars, or null when there is none (<c>current_bid</c>).</summary>
    public int? CurrentBid { get; set; }

    /// <summary>The dataset's count of bids placed (<c>bid_count</c>).</summary>
    public int BidCount { get; set; }

    /// <summary>The document's version, kept by the store (<c>_etag</c>).</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
// #endregion documents
