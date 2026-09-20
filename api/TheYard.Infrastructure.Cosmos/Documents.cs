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

    /// <summary>The experiment: the 100,000 expanded vehicles, under the tuned indexing policy.</summary>
    public const string Catalogue = "catalogue";

    /// <summary>The same 100,000 under the default policy, seeded once so the default's cost is measured.</summary>
    public const string CatalogueDefault = "catalogue-default";

    /// <summary>Site activity: hour counters and visitor counters, partitioned on the UTC day (ADR: Site activity, and the line an address does not cross).</summary>
    public const string Activity = "activity";

    /// <summary>The kept log: one document per request, error or warning, partitioned on the UTC day, expiring by the container's time-to-live (ADR: Logs that outlive the container).</summary>
    public const string Logs = "logs";

    /// <summary>Password reset links: one document per link under the GUID the link carries, expiring after the hour by the container's time-to-live, deleted on use (ADR: Accounts and per-user bids).</summary>
    public const string Resets = "resets";

    /// <summary>What the machines were doing: one document a minute from each site, partitioned on the UTC day, expiring after a month by the container's time-to-live (ADR: What the machines are doing).</summary>
    public const string Machines = "machines";

    /// <summary>Container name to partition key path, exactly as the definition files declare them.</summary>
    public static readonly IReadOnlyDictionary<string, string> PartitionKeyPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Vehicles] = "/make",
        [Photos] = "/style",
        [Bids] = "/user_id",
        [Users] = "/id",
        [Catalogue] = "/make",
        [CatalogueDefault] = "/make",
        [Activity] = "/day",
        [Logs] = "/day",
        [Resets] = "/id",
        [Machines] = "/day",
    };

    /// <summary>
    /// The four the site cannot come up without. The experiment containers are
    /// optional: a container that is missing or empty makes the experiment card
    /// say so, and changes nothing about the site (ADR: The partition key). The
    /// activity container is optional the same way: missing, the Admin tab's
    /// activity card says so and nothing is kept. So is the logs container,
    /// and so is the resets container: missing, minting a link fails and
    /// says so, and everything else on the site is untouched.
    /// </summary>
    public static readonly IReadOnlyList<string> Required = [Vehicles, Photos, Bids, Users];
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

    /// <summary>
    /// When the store last wrote it, in seconds since the epoch, set by the
    /// service and never by this code: null on the way in, so nothing is
    /// written, and the service's own value on the way back. The account
    /// store reads it to tell an orphaned claim from one whose account is a
    /// moment away (ADR: Accounts on a document store, addendum).
    /// </summary>
    [JsonPropertyName("_ts")]
    public long? Timestamp { get; set; }

    public static string IdFor(string normalizedEmail) => Prefix + normalizedEmail;
}
// #region activity-documents
/// <summary>
/// One store's requests in one UTC hour, partitioned on the day. Moved in
/// place by partial updates, so the counters add under two writers
/// (ADR: Site activity, and the line an address does not cross).
/// </summary>
public sealed class ActivityHourDocument
{
    public const string KindName = "hour";

    /// <summary>hour:{store}:{yyyy-MM-ddTHH}, so the same hour on the same store is always the same document.</summary>
    public string Id { get; set; } = "";
    public string Kind { get; set; } = KindName;
    public string Day { get; set; } = "";
    public string Store { get; set; } = "";
    public string Hour { get; set; } = "";
    public int Requests { get; set; }
    public int Bots { get; set; }
    public Dictionary<string, int> Paths { get; set; } = new(StringComparer.Ordinal);

    public static string IdFor(string store, DateTimeOffset hour) =>
        $"hour:{store}:{hour.ToUniversalTime():yyyy-MM-dd'T'HH}";
}

/// <summary>
/// One visitor token on one store on one UTC day, partitioned on the day. The
/// token is a keyed hash that rotates with the day; the network is the address
/// cut to three octets; nothing here can name a person.
/// </summary>
public sealed class ActivityVisitorDocument
{
    public const string KindName = "visitor";

    /// <summary>visitor:{store}:{token}. Unique within the day partition, which is what the token is scoped to.</summary>
    public string Id { get; set; } = "";
    public string Kind { get; set; } = KindName;
    public string Day { get; set; } = "";
    public string Store { get; set; } = "";
    public string Visitor { get; set; } = "";
    public string Network { get; set; } = "";
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int Requests { get; set; }
    public int Bots { get; set; }
    public Dictionary<string, int> Paths { get; set; } = new(StringComparer.Ordinal);

    public static string IdFor(string store, string visitor) => $"visitor:{store}:{visitor}";
}
// #endregion activity-documents

// #region log-documents
/// <summary>
/// One kept event, partitioned on the day it happened. Written once and never
/// updated, which is what makes a transactional batch of a hundred the right
/// write and the container's time-to-live the right retention (ADR: Logs that
/// outlive the container). Every string arrived through LogText.Clean, so no
/// field can carry an at sign.
/// </summary>
/// <summary>
/// One reset link, under the GUID the link carries, partitioned on that id.
/// <c>ttl</c> is the store's own field: the document expires that many
/// seconds after it is written, whatever else happens.
/// </summary>
public sealed class ResetLinkDocument
{
    public string Id { get; set; } = "";
    public string Token { get; set; } = "";
    public string Expires { get; set; } = "";
    public int Ttl { get; set; }
}

public sealed class LogDocument
{
    /// <summary>{at as ticks}:{random}, so two events in the same tick on two containers are two documents.</summary>
    public string Id { get; set; } = "";
    public string Day { get; set; } = "";
    public string At { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Store { get; set; } = "";
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int Status { get; set; }
    public long DurationMs { get; set; }
    public string Visitor { get; set; } = "";
    public string Network { get; set; } = "";
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
    public string TraceId { get; set; } = "";

    public static LogDocument From(LogEvent e, string day) => new()
    {
        Id = $"{e.At.UtcTicks}:{Guid.NewGuid():N}",
        Day = day,
        At = e.At.ToUniversalTime().ToString("O"),
        Kind = e.Kind,
        Store = e.Store,
        Level = e.Level,
        Category = e.Category,
        Method = e.Method,
        Path = e.Path,
        Status = e.Status,
        DurationMs = e.DurationMs,
        Visitor = e.Visitor,
        Network = e.Network,
        Message = e.Message,
        Detail = e.Detail,
        TraceId = e.TraceId,
    };

    public LogEvent ToEvent() => new(
        DateTimeOffset.Parse(At, null, System.Globalization.DateTimeStyles.RoundtripKind),
        Kind, Store, Level, Category, Method, Path, Status, DurationMs, Visitor, Network, Message, Detail, TraceId);
}
// #endregion log-documents

// #region machine-documents
/// <summary>
/// One minute of one site (ADR: What the machines are doing). Written once and
/// never updated, like a log line, and expired by the container. The three
/// bucket keys are written with the minute so that a day, a week and a month
/// are each one grouped query over a key the index already holds, rather than
/// forty thousand documents read back to be averaged here. A figure that was
/// not read is absent from the document, not null in it: the serializer leaves
/// nulls out, and an average in the store skips what is absent, which is the
/// same rule the chart follows when it breaks its line over a gap.
/// </summary>
public sealed class MachineMinuteDocument
{
    /// <summary>{site}:{minute}, so the two sites' minutes are two documents and a minute written twice is one.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";
    [JsonPropertyName("site")]
    public string Site { get; set; } = "";
    [JsonPropertyName("at")]
    public string At { get; set; } = "";
    [JsonPropertyName("b5")]
    public string B5 { get; set; } = "";
    [JsonPropertyName("h1")]
    public string H1 { get; set; } = "";
    [JsonPropertyName("h4")]
    public string H4 { get; set; } = "";
    [JsonPropertyName("limit_mb")]
    public double LimitMb { get; set; }
    [JsonPropertyName("ws_mb")]
    public double WsMb { get; set; }
    [JsonPropertyName("ws_max_mb")]
    public double WsMaxMb { get; set; }
    [JsonPropertyName("managed_mb")]
    public double ManagedMb { get; set; }
    [JsonPropertyName("cpu")]
    public double? Cpu { get; set; }
    [JsonPropertyName("cpu_max")]
    public double? CpuMax { get; set; }
    [JsonPropertyName("sql_cpu")]
    public double? SqlCpu { get; set; }
    [JsonPropertyName("sql_memory")]
    public double? SqlMemory { get; set; }
    [JsonPropertyName("sql_data_io")]
    public double? SqlDataIo { get; set; }
    [JsonPropertyName("ru")]
    public double Ru { get; set; }
    [JsonPropertyName("operations")]
    public int Operations { get; set; }
    [JsonPropertyName("requests")]
    public int Requests { get; set; }
    [JsonPropertyName("p50_ms")]
    public double? P50Ms { get; set; }
    [JsonPropertyName("p95_ms")]
    public double? P95Ms { get; set; }
    [JsonPropertyName("errors_5xx")]
    public int Errors5xx { get; set; }
    [JsonPropertyName("errors_4xx")]
    public int Errors4xx { get; set; }

    public static string IdFor(string site, DateTimeOffset minute) =>
        $"{site}:{minute.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)}";

    public static MachineMinuteDocument From(MachineMinute minute) => new()
    {
        Id = IdFor(minute.Site, minute.At),
        Day = MachineWindows.DayOf(minute.At),
        Site = minute.Site,
        At = minute.At.ToUniversalTime().ToString("O"),
        B5 = MachineWindows.KeyOf(minute.At, MachineGrain.FiveMinutes),
        H1 = MachineWindows.KeyOf(minute.At, MachineGrain.Hour),
        H4 = MachineWindows.KeyOf(minute.At, MachineGrain.FourHours),
        LimitMb = minute.MemoryLimitMb,
        WsMb = minute.WorkingSetMb,
        WsMaxMb = minute.WorkingSetMaxMb,
        ManagedMb = minute.ManagedMb,
        Cpu = minute.CpuPercent,
        CpuMax = minute.CpuMaxPercent,
        SqlCpu = minute.SqlCpuPercent,
        SqlMemory = minute.SqlMemoryPercent,
        SqlDataIo = minute.SqlDataIoPercent,
        Ru = minute.RequestUnits,
        Operations = minute.Operations,
        Requests = minute.Requests,
        P50Ms = minute.P50Ms,
        P95Ms = minute.P95Ms,
        Errors5xx = minute.ServerErrors,
        Errors4xx = minute.ClientErrors,
    };
}
// #endregion machine-documents
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
