// The Azure Container Registry card (ADR: One frame for every resource): storage against Basic's
// 10 GB and pulls a minute on the dials, the images and tags it holds, the untagged layers it is
// still paying to keep, named and never purged, and whether Basic still fits.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The registry's card.</summary>
public static class AcrCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["holds"] = "the registry's catalogue",
        ["pulled"] = "Azure Monitor",
        ["stale"] = "the registry's catalogue",
        ["tier"] = "kept minutes, Azure Monitor",
        ["Storage"] = "Azure Monitor",
        ["Storage p95, 30 d"] = CardFrame.KeptMinutes,
        ["Pulls, window"] = "Azure Monitor",
        ["Pulls failed"] = "Azure Monitor",
        ["Pushes, window"] = "Azure Monitor",
    };

    /// <summary>
    /// The pulls a minute Basic allows, which the pulls dial is read against. Typed, because the
    /// registry's usages name its size and its webhooks but not its read rate; the storage the
    /// other dial is read against is the registry's own Size limit, read each hour from listUsages.
    /// </summary>
    public const double BasicReadsPerMinute = 1_000;

    public static readonly IReadOnlyList<AzureMetric> Metrics =
    [
        new("StorageUsed", "average", "storage_gb", 1.0 / 1024 / 1024 / 1024, Interval: "PT1H"),
        new("TotalPullCount", "total", "pulls"),
        new("SuccessfulPullCount", "total", "pulls_ok"),
        new("SuccessfulPushCount", "total", "pushes"),
        new("RunDuration", "total", "run_ms"),
    ];

    public static readonly (double Amber, double Red) Storage = CloudThresholds.RegistryStorage;
    public static readonly (double Amber, double Red) Pulls = CloudThresholds.RegistryPulls;

    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? storageGb = double.TryParse(ResourceView.Fact(inputs, "size_limit_gb", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) ? limit : null;
        string storageWords = storageGb is { } l ? $"{l.ToString("0.##", CultureInfo.InvariantCulture)} GB" : "the size limit, not read yet";
        // The read rate is the Basic tier's, and applies only while the record says Basic; another
        // tier's rate is not read, and the dial says so instead of measuring against Basic's.
        double? readsPerMinute = string.Equals(ResourceView.Fact(inputs, "tier", ""), "Basic", StringComparison.OrdinalIgnoreCase) ? BasicReadsPerMinute : null;
        string readsWords = readsPerMinute is { } r ? $"{r:N0} a minute" : "a minute; the rate for this tier is not read";
        var dials = new[]
        {
            ResourceView.Dial(inputs, "storage_gb", "Storage", storageGb, Storage, v => v is { } g ? $"{g.ToString("0.00", CultureInfo.InvariantCulture)} of {storageWords}" : "not read"),
            ResourceView.Dial(inputs, "pulls", "Pulls, this minute", readsPerMinute, Pulls, v => v is { } p ? $"{p.ToString("0", CultureInfo.InvariantCulture)} of {readsWords}" : "not read"),
        };
        double? pulls = ResourceView.WindowSum(inputs, "pulls"), ok = ResourceView.WindowSum(inputs, "pulls_ok"), pushes = ResourceView.WindowSum(inputs, "pushes");
        var storageTone = PlanThresholds.ToneOf(ResourceView.Hour(inputs, "storage_gb").Select(v => ResourceView.Share(v, storageGb)).ToList(), Storage);
        var events = inputs.Hours.SelectMany(h => h.Events).ToList();
        var rows = new List<CloudRow>
        {
            new("holds", "What does it hold?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "repositories")} images, {ResourceView.Fact(inputs, "tags")} tags",
                ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("repository_rows") is null ? [new(ResourceView.Fact(inputs, "repository_list"), null)] : [], "good")
            { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("repository_rows"), "Image", "Type", "Tags") },
            new("pulled", "Who pulled?", pulls is null ? "not read yet" : $"{pulls.Value.ToString("N0", CultureInfo.InvariantCulture)} pulls",
                [new($"{ResourceView.Number(ok)} succeeded · {ResourceView.Number(pulls is { } all && ok is { } fine ? all - fine : null)} failed · {ResourceView.Number(pushes)} pushes in the window · pulled with the site's identity", null)],
                pulls is > 0 && ok is { } good && good < pulls ? "warning" : "good"),
            new("stale", "What is stale?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "untagged")} untagged manifests",
                [new($"{ResourceView.Fact(inputs, "untagged_gb")} GB no tag points at · a purge would free it; this card names it and does not run it", null)], "info"),
            new("tier", $"Is {ResourceView.Fact(inputs, "tier", "the tier")} still right?", inputs.Month.Count == 0 ? "not enough kept yet" : storageTone == PlanTone.Alarm ? "no, storage is past its line" : "yes, storage first",
                [new($"storage p95 {ResourceView.Number(ResourceFolding.Spread(inputs.Month, "storage_gb").P95, "0.00")} of {storageWords} over 30 d · {events.Count} events in the window", null)],
                storageTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "The registry",
            ResourceView.Uptime(inputs),
            $"up, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Storage", $"{ResourceView.Number(ResourceView.Now(inputs, "storage_gb"), "0.00")} GB of {storageWords}", CloudTones.Of(storageTone)),
                new("Storage p95, 30 d", $"{ResourceView.Number(ResourceFolding.Spread(inputs.Month, "storage_gb").P95, "0.00")} GB", "info"),
                new("Images", ResourceView.Fact(inputs, "repositories"), "info") { Source = "the registry's catalogue" },
                new("Tags", ResourceView.Fact(inputs, "tags"), "info") { Source = "the registry's catalogue" },
                new("Untagged", ResourceView.Fact(inputs, "untagged"), "info") { Source = "the registry's catalogue" },
                new("Untagged size", $"{ResourceView.Fact(inputs, "untagged_gb")} GB", "info") { Source = "the registry's catalogue" },
                new("Pulls, window", ResourceView.Number(pulls), "info"),
                new("Pulls failed", ResourceView.Number(pulls is { } p && ok is { } o ? p - o : null), pulls is { } p2 && ok is { } o2 && o2 < p2 ? "warning" : "info"),
                new("Pushes, window", ResourceView.Number(pushes), "info"),
                new("Tier", ResourceView.Fact(inputs, "tier"), "info") { Source = "ARM record" },
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one minute each; the catalogue hourly from the registry");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Container Registry",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "storage_gb", "Storage, 60 min", storageGb), ResourceView.Spark(inputs, "pulls", "Pulls, 60 min", readsPerMinute)],
            ResourceView.Chart(inputs, "Storage and pulls", ("storage_gb", "Storage", storageGb, false), ("pulls", "Pulls", readsPerMinute, false), Storage.Red, $"teal storage · green pulls · gold deploys · red event · dashed {Storage.Red} %"),
            rows,
            readout), Sources);
    }

    /// <summary>The hour's facts from the registry's record and its catalogue.</summary>
    /// <param name="record">The registry's record.</param>
    /// <param name="holdings">The catalogue, or null when it could not be read.</param>
    /// <param name="usages">The registry's listUsages answer, or null when it could not be read.</param>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement record, AcrHoldings? holdings, System.Text.Json.JsonElement? usages = null)
    {
        string? tier = AzureMetrics.Text(record, "sku.name");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tier"] = tier,
            ["size_limit_gb"] = SizeLimitGb(usages)?.ToString("0.##", CultureInfo.InvariantCulture),
            ["login_server"] = AzureMetrics.Text(record, "properties.loginServer"),
            ["repositories"] = holdings?.Repositories.Count.ToString(CultureInfo.InvariantCulture),
            ["tags"] = holdings?.Repositories.Sum(r => r.Tags).ToString(CultureInfo.InvariantCulture),
            ["repository_rows"] = holdings is null ? null : CloudTable.ToFact(holdings.Repositories.Select(r => new[] { r.Name, "Image repository", r.Tags.ToString(CultureInfo.InvariantCulture) })),
            ["repository_list"] = holdings is null ? "the catalogue could not be read; AcrPull on the registry is what it needs" : string.Join(" · ", holdings.Repositories.Select(r => $"{r.Name} {r.Tags} tags")),
            ["untagged"] = holdings?.UntaggedManifests.ToString(CultureInfo.InvariantCulture),
            ["untagged_gb"] = holdings is null ? null : (holdings.UntaggedBytes / 1024.0 / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture),
            ["spec"] = string.Join(' ', new[] { tier, AzureMetrics.Text(record, "location") }.Where(p => p is not null)).ToUpperInvariant(),
        };
    }

    /// <summary>The registry's Size limit in gigabytes, from its listUsages answer; null when the answer has none.</summary>
    /// <param name="usages">The listUsages answer.</param>
    public static double? SizeLimitGb(System.Text.Json.JsonElement? usages) =>
        usages is { } answer && answer.TryGetProperty("value", out var list) && list.ValueKind == System.Text.Json.JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(usage => string.Equals(AzureMetrics.Text(usage, "name"), "Size", StringComparison.OrdinalIgnoreCase))
                .Select(usage => usage.TryGetProperty("limit", out var bytes) && bytes.TryGetDouble(out double b) ? b / 1024 / 1024 / 1024 : (double?)null)
                .FirstOrDefault()
            : null;
}
