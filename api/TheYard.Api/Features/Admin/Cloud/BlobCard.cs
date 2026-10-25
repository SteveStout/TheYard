// The blob service card (ADR: One frame for every resource, the addendum on the storage cards): the blog's
// pictures against a line that is not a quota and the share of picture reads the host answers from memory
// on the dials; reads, reads from memory and writes for sixty minutes; blob reads a day for thirty days;
// what is in it, what it costs, whether it answers, and who put the last picture in. Red at the daily read
// line, or when public access is anything but none, which is a security read and needs him.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The lines the blob card is read against, from configuration (Admin:BlobPicturesAmberBytes, Admin:BlobPicturesRedBytes, Admin:BlobDailyReadLine).</summary>
/// <param name="PicturesAmberBytes">The pictures' bytes at which the card goes amber: a line, not a quota.</param>
/// <param name="PicturesRedBytes">The pictures' bytes at which it goes red.</param>
/// <param name="DailyReads">Blob reads a day at which it goes red.</param>
public sealed record BlobLines(double PicturesAmberBytes, double PicturesRedBytes, double DailyReads)
{
    /// <summary>The lines as configuration gives them, or the mockup's figures: 1 GB, 1.8 GB, 24,000 reads.</summary>
    public static BlobLines From(IConfiguration configuration) => new(
        configuration.GetValue("Admin:BlobPicturesAmberBytes", 1_000_000_000d),
        configuration.GetValue("Admin:BlobPicturesRedBytes", 1_800_000_000d),
        configuration.GetValue("Admin:BlobDailyReadLine", 24_000d));
}

/// <summary>The blob service's card.</summary>
public static class BlobCard
{
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["in"] = "the blob service's container listing, once an hour",
        ["cost"] = CardFrame.TheBill,
        ["answering"] = "the host's own timing of the listing",
        ["last"] = "the blog container's listing",
        ["Access tier"] = "Azure Resource Manager",
        ["Public access"] = "Azure Resource Manager",
        ["Kept"] = "the container listings",
        ["Cache in the host"] = CardFrame.ThisProcess,
        ["Egress this month"] = "Azure Monitor, Egress",
        ["Lifecycle rule"] = "Azure Resource Manager",
        ["The read line"] = "configuration, Admin:BlobDailyReadLine",
        ["Cost"] = CardFrame.TheBill,
        ["Live read"] = CardFrame.ThisProcess,
    };

    public const string ReadFilter = "ApiName eq 'GetBlob'";
    public const string WriteFilter = "ApiName eq 'PutBlob' or ApiName eq 'PutBlock' or ApiName eq 'PutBlockList' or ApiName eq 'CopyBlob'";

    public static readonly IReadOnlyList<AzureMetric> Metrics =
    [
        new("Transactions", "total", "transactions"),
        new("Transactions", "total", "reads", Filter: ReadFilter),
        new("Transactions", "total", "writes", Filter: WriteFilter),
        new("Egress", "total", "egress_bytes"),
        new("BlobCapacity", "average", "bytes", Interval: "PT1H"),
        new("BlobCount", "average", "blobs", Interval: "PT1H"),
    ];

    /// <summary>The free egress a month, which the readout reads the month's egress against.</summary>
    public const double FreeEgressGb = 100;

    public static CloudReport View(ResourceCardInputs inputs, BlobLines lines)
    {
        double? pictures = Number(ResourceView.Fact(inputs, "pictures_bytes", ""));
        double? pictureCount = Number(ResourceView.Fact(inputs, "pictures_count", ""));
        double? share = ResourceView.Share(pictures, lines.PicturesAmberBytes);
        double redShare = 100 * lines.PicturesRedBytes / lines.PicturesAmberBytes;
        string picturesTone = pictures is not { } b ? "info" : b >= lines.PicturesRedBytes ? "alarm" : b >= lines.PicturesAmberBytes ? "warning" : "good";
        var dials = new[]
        {
            new CloudDial("The blog's pictures", share, pictures is { } p ? Bytes(p) : "none yet",
                pictures is null ? "no blog container yet; the pictures land with the blog's pictures lane" : $"{pictureCount:N0} pictures · of the {Bytes(lines.PicturesAmberBytes)} line, not a quota · red {Bytes(lines.PicturesRedBytes)}",
                100, redShare, null, null, picturesTone),
            new CloudDial("Served from memory", null, "not read", "the host's picture cache arrives with the blog's pictures; a read it answers costs no transaction · amber under 70 % · red under 50 %", 70, 50, null, null, "info"),
        };
        var reads = Series.Decode(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("reads_hourly"));
        var days = Series.Daily(reads);
        double? today = days.Count > 0 ? days[^1].Value : null;
        bool overReads = days.TakeLast(2).Any(d => d.Value >= lines.DailyReads);
        string publicAccess = ResourceView.Fact(inputs, "public_access", "not read");
        bool exposed = publicAccess != "none" && publicAccess != "not read";
        var rows = new List<CloudRow>
        {
            new("in", "What is in it?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "container_count", "0")} containers",
                [new(Refused(inputs) is { } why ? $"the listing was refused ({why}): the site's identity needs Storage Blob Data Reader on the account" : $"public access {publicAccess} · each container's purpose is its metadata tag", null)],
                exposed ? "alarm" : "info") { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("container_rows"), "Container", "Blobs", "Bytes", "What it is for", "Public access") },
            ResourceView.Fact(inputs, ShareCard.CostUnread, "") == "yes"
                ? new("cost", "What does it cost?", "not read yet", [new(ShareCard.CostUnreadWords, null)], "info")
                : new("cost", "What does it cost?", ResourceView.Fact(inputs, "cost_month", "") is { Length: > 0 } month ? $"${month}" : "not on the bill yet",
                    [new("this month, the whole storage account: the bill keeps one line an account, so the blob meters and the share's are not split here", null)], "info"),
            new("answering", "Is it answering?", ResourceView.Fact(inputs, "store_ms", "") is { Length: > 0 } ms ? $"{ms} ms" : "not read yet",
                [new($"the last listing {ResourceView.Fact(inputs, "store_at", "not read")}, status {ResourceView.Fact(inputs, "store_status", "not read")} · the host serves no picture yet", null)],
                ResourceView.Fact(inputs, "store_status", "200") is "200" or "not read" ? "good" : "warning"),
            new("last", "Who put the last picture in?", ResourceView.Fact(inputs, "newest_at", "") is { Length: > 0 } at ? at : "no pictures yet",
                [new("the newest pictures in the blog container, by the listing's own time", null)], "info") { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("newest_rows"), "Picture", "Bytes", "Put in") },
        };
        if (overReads)
        {
            rows[2] = rows[2] with { Tone = "alarm", Detail = [.. rows[2].Detail, new CloudLink($"blob reads passed {lines.DailyReads:N0} in a day", null)] };
        }
        double? egress = Number(ResourceView.Fact(inputs, "egress_month_bytes", ""));
        var readout = new CloudReadout(
            "The blob service, now",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Access tier", ResourceView.Fact(inputs, "access_tier"), "info"),
                new("Public access", publicAccess == "none" ? "None; the site's identity reads" : publicAccess, exposed ? "alarm" : "info"),
                new("Kept", Number(ResourceView.Fact(inputs, "kept_bytes", "")) is { } kept ? $"{Bytes(kept)} in {ResourceView.Fact(inputs, "kept_blobs", "0")} blobs" : "not read", "info"),
                new("Cache in the host", "not built yet: the blog's pictures lane adds it", "info"),
                new("Egress this month", egress is { } e ? $"{(e / 1e9).ToString("0.##", CultureInfo.InvariantCulture)} GB of the free {FreeEgressGb:0} GB" : "not read", "info"),
                new("Lifecycle rule", ResourceView.Fact(inputs, "lifecycle"), "info"),
                new("The read line", $"{lines.DailyReads:N0} reads a day, the card's red line; today {ResourceView.Number(today)}", overReads ? "alarm" : "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            "Blob storage charges for keeping and for each read; a picture named by the hash of its bytes is cached a year, so most reads never reach it.");
        var chart = new CloudChart("Blob reads each day, last 30 days",
            [new("Blob reads a day", [.. days.Select(d => new CloudPoint(d.Day.ToUnixTimeMilliseconds(), ResourceView.Share(d.Value, lines.DailyReads)))]),
             new("Bytes kept", [.. inputs.Buckets.Select(b => new CloudPoint(b.At.ToUnixTimeMilliseconds(), ResourceView.Share(b.Means.GetValueOrDefault("bytes"), lines.PicturesAmberBytes)))])],
            [.. inputs.Hours.SelectMany(h => h.Events).Select(e => new CloudMark(e.At.ToUnixTimeMilliseconds(), "event", $"{e.Title} on {e.Resource}"))],
            100, $"teal blob reads a day · green bytes kept against the pictures' line · red {lines.DailyReads:N0} reads a day",
            days.Count > 0 ? days[0].Day.ToUnixTimeMilliseconds() : (inputs.Now - TimeSpan.FromDays(30)).ToUnixTimeMilliseconds());
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0),
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The blob service",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "reads", "Reads, 60 min", null), new CloudSpark("From memory, 60 min", []), ResourceView.Spark(inputs, "writes", "Writes, 60 min", null)],
            chart,
            rows,
            readout);
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>The account's record: tier, redundancy and whether a blob may be public.</summary>
    public static void AccountFacts(Dictionary<string, string?> facts, JsonElement account)
    {
        string sku = AzureMetrics.Text(account, "sku.name") ?? "";
        facts["access_tier"] = AzureMetrics.Text(account, "properties.accessTier");
        facts["public_access"] = AzureMetrics.Text(account, "properties.allowBlobPublicAccess") == "true" ? "allowed on the account" : "none";
        facts["spec"] = string.Join(" · ", new[] { $"Blob storage on {AzureMetrics.Text(account, "name")}", facts["access_tier"], sku.Split('_').LastOrDefault(), $"public access {facts["public_access"]}" }.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>The containers Resource Manager lists: name, public access and the purpose tag.</summary>
    public static List<(string Name, string Access, string? Purpose)> Containers(JsonElement list) =>
        list.TryGetProperty("value", out var value)
            ? [.. value.EnumerateArray().Select(c => (AzureMetrics.Text(c, "name") ?? "?", AzureMetrics.Text(c, "properties.publicAccess") ?? "None", AzureMetrics.Text(c, "properties.metadata.purpose")))]
            : [];

    /// <summary>What the listings found: each container's blobs and bytes, the blog's pictures, the newest of them, and how the store answered.</summary>
    public static void ListingFacts(Dictionary<string, string?> facts, IReadOnlyList<(string Name, string Access, string? Purpose)> containers, IReadOnlyList<ContainerListing> listed, (DateTimeOffset At, int Status, long Ms)? last = null)
    {
        facts["container_count"] = containers.Count.ToString(CultureInfo.InvariantCulture);
        if (containers.Any(c => !string.Equals(c.Access, "None", StringComparison.OrdinalIgnoreCase)))
        {
            facts["public_access"] = "a container allows it";
        }
        var byName = listed.ToDictionary(l => l.Name, StringComparer.Ordinal);
        facts["container_rows"] = CloudTable.ToFact(containers.Select(c => byName.TryGetValue(c.Name, out var l) && l.Blobs is { } n
            ? new[] { c.Name, n.ToString("N0", CultureInfo.InvariantCulture), Bytes(l.Bytes), c.Purpose ?? "no purpose tag", c.Access }
            : [c.Name, "not read", "not read", c.Purpose ?? "no purpose tag", c.Access]));
        facts["kept_bytes"] = listed.Sum(l => l.Bytes).ToString(CultureInfo.InvariantCulture);
        facts["kept_blobs"] = listed.Sum(l => l.Blobs ?? 0).ToString(CultureInfo.InvariantCulture);
        if (listed.FirstOrDefault(l => l.Refused is not null) is { } refused)
        {
            facts["refused_listing"] = refused.Refused;
        }
        if (byName.TryGetValue("blog", out var blog) && blog.Blobs is { } count)
        {
            facts["pictures_bytes"] = blog.Bytes.ToString(CultureInfo.InvariantCulture);
            facts["pictures_count"] = count.ToString(CultureInfo.InvariantCulture);
            facts["newest_rows"] = CloudTable.ToFact(blog.Newest.Select(b => new[] { b.Name, Bytes(b.Bytes), b.At.UtcDateTime.ToString("d MMM HH:mm", CultureInfo.InvariantCulture) }));
            facts["newest_at"] = blog.Newest.Count > 0 ? blog.Newest[0].At.UtcDateTime.ToString("d MMM HH:mm 'UTC'", CultureInfo.InvariantCulture) : null;
        }
        if (last is { } read)
        {
            facts["store_at"] = read.At.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
            facts["store_status"] = read.Status.ToString(CultureInfo.InvariantCulture);
            facts["store_ms"] = read.Ms.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string? Refused(ResourceCardInputs inputs) => ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("refused_listing");

    /// <summary>Bytes the way the card prints them: KB, MB or GB.</summary>
    public static string Bytes(double bytes) =>
        bytes >= 1e9 ? $"{(bytes / 1e9).ToString("0.#", CultureInfo.InvariantCulture)} GB"
        : bytes >= 1e6 ? $"{(bytes / 1e6).ToString("0.#", CultureInfo.InvariantCulture)} MB"
        : bytes >= 1e3 ? $"{(bytes / 1e3).ToString("0", CultureInfo.InvariantCulture)} KB"
        : $"{bytes.ToString("0", CultureInfo.InvariantCulture)} B";

    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}
