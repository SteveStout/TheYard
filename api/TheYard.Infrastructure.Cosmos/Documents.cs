using System.Text.Json.Serialization;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure.Cosmos;

// #region containers
/// <summary>
/// The containers this store uses and the partition key each one is built on.
/// The authority is `infra/cosmos/<name>.json`, which a person applies with the
/// Azure CLI; this catalog is what the adapters use, and a test holds the two
/// together (ADR: The partition key). Why the definition and not the code is
/// the authority is the addendum to ADR: Data first, and the database in source
/// control.
/// </summary>
public static class Containers
{
    public const string Vehicles = "vehicles";
    public const string Photos = "photos";
    public const string Bids = "bids";
    public const string Users = "users";

    /// <summary>Container name to partition key path, exactly as the definition files declare them.</summary>
    public static readonly IReadOnlyDictionary<string, string> PartitionKeyPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Vehicles] = "/make",
        [Photos] = "/style",
        [Bids] = "/user_id",
        [Users] = "/id",
    };
}
// #endregion containers

// #region documents
// Document shapes. Snake case on the wire, like the dataset and the API, which
// is what makes a document readable beside the JSON it was seeded from. There
// is no mapper between these and the domain records except the dull kind that
// copies fields one at a time, for the same reason VehicleRows exists on the
// relational side: the interesting failure of a persistence layer is a field
// that quietly stops being copied.

/// <summary>
/// One seed vehicle. Partitioned on <c>make</c>. It does not carry the dataset's
/// <c>images</c>: every body style in the seed has a photo pool, so
/// <c>PhotoGallery.SelectPhotos</c> replaces those URLs for every vehicle and
/// they were 30 per cent of every document for nothing (ADR: A second store on
/// Cosmos DB, and what it costs).
/// </summary>
public sealed class VehicleDocument
{
    public string Id { get; set; } = "";
    public int Seq { get; set; }
    public string Vin { get; set; } = "";
    public int Year { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string Trim { get; set; } = "";
    public string BodyStyle { get; set; } = "";
    public string ExteriorColor { get; set; } = "";
    public string InteriorColor { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Transmission { get; set; } = "";
    public string Drivetrain { get; set; } = "";
    public int OdometerKm { get; set; }
    public string FuelType { get; set; } = "";
    public double ConditionGrade { get; set; }
    public string ConditionReport { get; set; } = "";
    public List<string> DamageNotes { get; set; } = [];
    public string TitleStatus { get; set; } = "";
    public string Province { get; set; } = "";
    public string City { get; set; } = "";
    public string AuctionStart { get; set; } = "";
    public int StartingBid { get; set; }
    public int? ReservePrice { get; set; }
    public int? BuyNowPrice { get; set; }
    public string SellingDealership { get; set; } = "";
    public string Lot { get; set; } = "";
    public int? CurrentBid { get; set; }
    public int BidCount { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>One photo manifest entry. Partitioned on <c>style</c>, which is how the loader groups them.</summary>
public sealed class PhotoDocument
{
    /// <summary>The file name, which is unique in the manifest and is the id for that reason.</summary>
    public string Id { get; set; } = "";
    public int Seq { get; set; }
    public string Style { get; set; } = "";
    public string Title { get; set; } = "";

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// One buyer's standing on one vehicle. The id is the vehicle id and the
/// partition is the buyer, so the pair that is the primary key on SQL Server is
/// the (partition, id) pair here, and every read and write is a point operation.
/// </summary>
public sealed class BidDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Amount { get; set; }
    public int BidCount { get; set; }
    public bool WonBuyNow { get; set; }
    public long AtMs { get; set; }

    /// <summary>
    /// The concurrency token, kept by the store. <c>rowversion</c> on SQL Server,
    /// a token the store moves on SQLite, and the document's own etag here: a
    /// replace that sends a stale one is refused with 412
    /// (ADR: The SQL Server backend).
    /// </summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// One account, whole, in one document. Identity's relational shape is a user
/// row plus claims, logins, tokens and roles in separate tables; this application
/// uses none of those tables, so the document holds exactly the columns the user
/// row holds that it reads (ADR: Accounts on a document store).
/// </summary>
public sealed class UserDocument
{
    public string Id { get; set; } = "";
    public string? UserName { get; set; }
    public string? NormalizedUserName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }
    public long CreatedAtMs { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// The claim on an email address. A store with no unique index across
/// partitions cannot promise two accounts will not share an address; a document
/// whose id is the address can, because a second create of the same id is
/// refused with 409. Registering writes this first and the account second
/// (ADR: Accounts on a document store).
/// </summary>
public sealed class EmailClaimDocument
{
    public const string Prefix = "email:";

    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";

    public static string IdFor(string normalizedEmail) => Prefix + normalizedEmail;
}
// #endregion documents

/// <summary>Document to domain and back, field by field, in one place.</summary>
public static class Documents
{
    // #region mapping
    public static Vehicle ToVehicle(this VehicleDocument d) => new()
    {
        Id = d.Id,
        Vin = d.Vin,
        Year = d.Year,
        Make = d.Make,
        Model = d.Model,
        Trim = d.Trim,
        BodyStyle = d.BodyStyle,
        ExteriorColor = d.ExteriorColor,
        InteriorColor = d.InteriorColor,
        Engine = d.Engine,
        Transmission = d.Transmission,
        Drivetrain = d.Drivetrain,
        OdometerKm = d.OdometerKm,
        FuelType = d.FuelType,
        ConditionGrade = d.ConditionGrade,
        ConditionReport = d.ConditionReport,
        DamageNotes = d.DamageNotes,
        TitleStatus = d.TitleStatus,
        Province = d.Province,
        City = d.City,
        AuctionStart = d.AuctionStart,
        StartingBid = d.StartingBid,
        ReservePrice = d.ReservePrice,
        BuyNowPrice = d.BuyNowPrice,
        // Not stored: the gallery derives them (see VehicleDocument).
        Images = [],
        SellingDealership = d.SellingDealership,
        Lot = d.Lot,
        CurrentBid = d.CurrentBid,
        BidCount = d.BidCount,
    };

    public static VehicleDocument ToDocument(this Vehicle v, int seq) => new()
    {
        Id = v.Id,
        Seq = seq,
        Vin = v.Vin,
        Year = v.Year,
        Make = v.Make,
        Model = v.Model,
        Trim = v.Trim,
        BodyStyle = v.BodyStyle,
        ExteriorColor = v.ExteriorColor,
        InteriorColor = v.InteriorColor,
        Engine = v.Engine,
        Transmission = v.Transmission,
        Drivetrain = v.Drivetrain,
        OdometerKm = v.OdometerKm,
        FuelType = v.FuelType,
        ConditionGrade = v.ConditionGrade,
        ConditionReport = v.ConditionReport,
        DamageNotes = [.. v.DamageNotes],
        TitleStatus = v.TitleStatus,
        Province = v.Province,
        City = v.City,
        AuctionStart = v.AuctionStart,
        StartingBid = v.StartingBid,
        ReservePrice = v.ReservePrice,
        BuyNowPrice = v.BuyNowPrice,
        SellingDealership = v.SellingDealership,
        Lot = v.Lot,
        CurrentBid = v.CurrentBid,
        BidCount = v.BidCount,
    };

    public static PhotoEntry ToEntry(this PhotoDocument d) => new(d.Id, d.Style, d.Title);

    public static PhotoDocument ToDocument(this PhotoEntry p, int seq) => new() { Id = p.File, Seq = seq, Style = p.Style, Title = p.Title };

    public static StoredBid ToStoredBid(this BidDocument d) =>
        new(d.UserId, d.Id, new BidState(d.Amount, d.BidCount, d.WonBuyNow, d.AtMs));
    // #endregion mapping
}
