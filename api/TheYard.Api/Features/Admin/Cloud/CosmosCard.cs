// The Azure Cosmos DB card (ADR: One frame for every resource): request units a second against
// the free thousand and storage against the free 25 GB on the dials, throttled requests on the
// chart, the containers it keeps and their expiries, and whether the free tier still fits.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The document store's card.</summary>
public static class CosmosCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runs"] = "ARM record",
        ["throttling"] = "Azure Monitor",
        ["keeping"] = "Azure Monitor",
        ["tier"] = "kept minutes, Azure Monitor",
        ["RU/s now"] = "Azure Monitor",
        ["RU/s p95, 30 d"] = CardFrame.KeptMinutes,
        ["Normalized peak"] = "Azure Monitor",
        ["Storage"] = "Azure Monitor",
        ["Documents"] = "Azure Monitor",
        ["Requests, hour"] = "Azure Monitor",
        ["Throttled"] = "Azure Monitor",
        ["Latency"] = "Azure Monitor",
    };

    /// <summary>
    /// The free tier's storage, which the storage dial is read against. Typed, because no record
    /// Azure returns for the account states it; the throughput the RU dial is read against is the
    /// account's own provisioned figure, read each hour.
    /// </summary>
    public const double FreeGb = 25;

    /// <summary>The metrics the card keeps.</summary>
    public static readonly IReadOnlyList<AzureMetric> Metrics =
    [
        new("TotalRequestUnits", "total", "ru_per_s", 1.0 / 60),
        new("NormalizedRUConsumption", "maximum", "normalized"),
        new("DataUsage", "total", "storage_gb", 1.0 / 1024 / 1024 / 1024, Interval: "PT5M"),
        new("DocumentCount", "total", "documents", Interval: "PT5M"),
        new("ServerSideLatency", "average", "latency_ms"),
        new("TotalRequests", "count", "requests"),
        new("TotalRequests", "count", "throttled", Filter: "StatusCode eq '429'"),
        // The live tests' copies (tests-*) share the database's throughput: the gate's own suites
        // made nearly nine in ten of the throttles the card used to count as the site's, so the card
        // reads them apart and judges the site by its own (the ADR has the measurement).
        new("TotalRequests", "count", "requests_tests", Filter: "CollectionName sw 'tests-'"),
        new("TotalRequests", "count", "throttled_tests", Filter: "StatusCode eq '429' and CollectionName sw 'tests-'"),
    ];

    /// <summary>
    /// The share of the site's own requests throttled at which the card turns amber: Azure's guidance
    /// reads one to five per cent of requests answered 429 and retried by the SDK as healthy, so the
    /// line sits at the bottom of that band.
    /// </summary>
    public const double ThrottledAmberShare = 0.01;

    /// <summary>The site's own throttles and requests over the window: every container's less the live tests' copies.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static (double? Throttled, double? Requests, double? TestsThrottled) SiteThrottling(ResourceCardInputs inputs)
    {
        double? all = ResourceView.WindowSum(inputs, "throttled");
        double? tests = ResourceView.WindowSum(inputs, "throttled_tests");
        double? requests = ResourceView.WindowSum(inputs, "requests");
        double testRequests = ResourceView.WindowSum(inputs, "requests_tests") ?? 0;
        return (all is { } a ? Math.Max(0, a - (tests ?? 0)) : null, requests is { } r ? Math.Max(0, r - testRequests) : null, tests);
    }

    /// <summary>
    /// The throttling row's tone: warning when the site's own throttles reach ThrottledAmberShare of
    /// its requests, or when there are throttles and no requests read to weigh them against;
    /// otherwise the throughput's own tone. No throttles read leaves the tone to the throughput.
    /// </summary>
    public static string ThrottleTone(double? throttled, double? requests, PlanTone ruTone) =>
        throttled is > 0 && (requests is not > 0 || throttled.Value / requests.Value >= ThrottledAmberShare) ? "warning"
        : CloudTones.Of(ruTone == PlanTone.Info ? PlanTone.Good : ruTone);

    /// <summary>RU/s amber at 70 per cent of the thousand and red at 90; storage amber at 70 and red at 90.</summary>
    public static readonly (double Amber, double Red) Throughput = CloudThresholds.CosmosThroughput;
    public static readonly (double Amber, double Red) Storage = CloudThresholds.CosmosStorage;

    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? provisioned = double.TryParse(ResourceView.Fact(inputs, "throughput", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double ru) ? ru : null;
        string limitWords = provisioned is { } p ? $"{p.ToString("N0", CultureInfo.InvariantCulture)} RU/s" : "the provisioned RU/s, not read yet";
        // The free tier's storage allowance applies only while the account's record says the free
        // tier is on; off it, storage has no ceiling this card knows, and the dial says so.
        double? storageLimit = ResourceView.Fact(inputs, "free_tier", "") == "on" ? FreeGb : null;
        string storageWords = storageLimit is { } s ? $"{s} GB" : "GB, no free-tier allowance on this account";
        var dials = new[]
        {
            ResourceView.Dial(inputs, "ru_per_s", "RU/s, right now", provisioned, Throughput, v => v is { } r ? $"{r.ToString("0", CultureInfo.InvariantCulture)} of {limitWords}" : "not read"),
            ResourceView.Dial(inputs, "storage_gb", "Storage", storageLimit, Storage, v => v is { } g ? $"{g.ToString("0.00", CultureInfo.InvariantCulture)} of {storageWords}" : "not read"),
        };
        var (throttled, siteRequests, testsThrottled) = SiteThrottling(inputs);
        string throttleShare = throttled is { } t && siteRequests is > 0 ? $"{(t / siteRequests.Value * 100).ToString("0.00", CultureInfo.InvariantCulture)} % of the site's requests" : "no requests read";
        static double SiteOf(ResourceBucket b) => Math.Max(0, (b.Sums.GetValueOrDefault("throttled") ?? 0) - (b.Sums.GetValueOrDefault("throttled_tests") ?? 0));
        var throttles = inputs.Buckets.Where(b => SiteOf(b) > 0)
            .Select(b => new CloudMark(b.At.ToUnixTimeMilliseconds(), "event", $"{SiteOf(b):0} of the site's requests throttled (429)"));
        var events = inputs.Hours.SelectMany(h => h.Events).ToList();
        var ruTone = PlanThresholds.ToneOf(ResourceView.Hour(inputs, "ru_per_s").Select(v => ResourceView.Share(v, provisioned)).ToList(), Throughput);
        string throttleTone = ThrottleTone(throttled, siteRequests, ruTone);
        var rows = new List<CloudRow>
        {
            new("runs", "What runs on it?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "container_count")} containers",
                [], "good") { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("container_rows"), "Container", "Type", "Kept for") },
            new("throttling", "Is it throttling?", throttled is null ? "not read yet" : $"{throttled.Value.ToString("N0", CultureInfo.InvariantCulture)} throttled",
                [new($"the site's 429s in the window, {throttleShare}, retried by the SDK · amber at {ThrottledAmberShare * 100:0} % · normalized peak {PlanView.Percent(ResourceFolding.Spread(inputs.Month, "normalized").Peak)} · latency {ResourceView.Number(ResourceView.Now(inputs, "latency_ms"), "0.0")} ms", null),
                    new(testsThrottled is { } tt ? $"the live tests' copies (tests-*) were throttled {ResourceView.Number(tt)} more times on the same shared throughput, counted apart" : "the live tests' copies (tests-*): not read yet, so every throttle above is counted as the site's", null)],
                throttleTone),
            new("keeping", "What is it keeping?", ResourceView.Now(inputs, "documents") is { } docs ? $"{docs.ToString("N0", CultureInfo.InvariantCulture)} documents" : "not read yet",
                [new($"storage {ResourceView.Number(ResourceView.Now(inputs, "storage_gb"), "0.00")} GB · every kept container expires its own documents", null)], "info"),
            new("tier", "Is the free tier still right?", inputs.Month.Count == 0 ? "not enough kept yet" : ruTone == PlanTone.Alarm ? "no, throughput is past its line" : "yes, throughput first",
                [new($"RU/s p95 {ResourceView.Number(ResourceFolding.Spread(inputs.Month, "ru_per_s").P95, "0")} of {limitWords} over 30 d · free tier {ResourceView.Fact(inputs, "free_tier")} · red at {Throughput.Red} % held {PlanThresholds.Sustained} min", null)],
                ruTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "The account",
            ResourceView.Uptime(inputs),
            $"up, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("RU/s now", ResourceView.Number(ResourceView.Now(inputs, "ru_per_s"), "0"), CloudTones.Of(ruTone)),
                new("RU/s p95, 30 d", ResourceView.Number(ResourceFolding.Spread(inputs.Month, "ru_per_s").P95, "0"), "info"),
                new("Normalized peak", PlanView.Percent(ResourceFolding.Spread(inputs.Month, "normalized").Peak), "info"),
                new("Storage", storageLimit is { } free ? $"{ResourceView.Number(ResourceView.Now(inputs, "storage_gb"), "0.00")} / {free} GB" : $"{ResourceView.Number(ResourceView.Now(inputs, "storage_gb"), "0.00")} GB", "info"),
                new("Documents", ResourceView.Number(ResourceView.Now(inputs, "documents")), "info"),
                new("Requests, hour", ResourceView.Number(ResourceView.HourSum(inputs, "requests") is { } hourAll ? (double?)Math.Max(0, hourAll - (ResourceView.HourSum(inputs, "requests_tests") ?? 0)) : null), "info"),
                new("Throttled", ResourceView.Number(throttled), throttleTone == "warning" ? "warning" : "info"),
                new("Latency", $"{ResourceView.Number(ResourceView.Now(inputs, "latency_ms"), "0.0")} ms", "info"),
                new("Containers", ResourceView.Fact(inputs, "container_count"), "info") { Source = "ARM record" },
                new("Free tier", ResourceView.Fact(inputs, "free_tier"), "info") { Source = "ARM record" },
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one minute each; live every 60 s from Azure Monitor");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Cosmos DB",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "ru_per_s", "RU/s, 60 min", provisioned), ResourceView.Spark(inputs, "normalized", "Normalized RU, 60 min", 100, percentReading: true)],
            ResourceView.Chart(inputs, "RU/s and storage", ("ru_per_s", "RU/s", provisioned, false), ("storage_gb", "Storage", storageLimit, false), Throughput.Red, $"teal RU/s · green storage · gold deploys · red 429s · dashed {Throughput.Red} %", throttles),
            rows,
            readout), Sources);
    }

    /// <summary>A container's time to live in words: off, on with no default, or the default in days or hours.</summary>
    public static string TimeToLive(string? seconds) =>
        seconds is null ? "kept until deleted"
        : seconds == "-1" ? "on, no default"
        : double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
            ? s >= 86_400 ? $"{(s / 86_400).ToString("0.#", CultureInfo.InvariantCulture)} days" : $"{(s / 3_600).ToString("0.#", CultureInfo.InvariantCulture)} hours"
            : seconds;

    /// <summary>The hour's facts from the account's record and its containers' definitions.</summary>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement account, System.Text.Json.JsonElement containers, string? throughput = null)
    {
        var list = containers.TryGetProperty("value", out var value)
            ? value.EnumerateArray().Select(c => (Name: AzureMetrics.Text(c, "name") ?? "?", Ttl: AzureMetrics.Text(c, "properties.resource.defaultTtl"))).OrderBy(c => c.Name, StringComparer.Ordinal).ToList()
            : [];
        string? free = AzureMetrics.Text(account, "properties.enableFreeTier");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["container_count"] = list.Count.ToString(CultureInfo.InvariantCulture),
            ["containers"] = string.Join(" · ", list.Select(c => c.Ttl is null or "-1" ? c.Name : $"{c.Name} {(double.Parse(c.Ttl, CultureInfo.InvariantCulture) / 86_400).ToString("0.#", CultureInfo.InvariantCulture)} d")),
            ["container_rows"] = CloudTable.ToFact(list.Select(c => new[] { c.Name, "Cosmos DB container", TimeToLive(c.Ttl) })),
            ["free_tier"] = free == "true" ? "on" : free == "false" ? "off" : null,
            ["throughput"] = throughput,
            ["spec"] = string.Join(' ', new[] { free == "true" ? "FREE TIER" : null, double.TryParse(throughput, CultureInfo.InvariantCulture, out double ru) ? $"{ru.ToString("N0", CultureInfo.InvariantCulture)} RU/S SHARED" : null, AzureMetrics.Text(account, "location")?.ToUpperInvariant() }.Where(p => p is not null)),
        };
    }
}
