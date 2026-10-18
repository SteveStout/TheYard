// The Azure Container Registry card (ADR: One frame for every resource): storage against Basic's
// 10 GB and pulls a minute on the dials, the images and tags it holds, the untagged layers it is
// still paying to keep, named and never purged, and whether Basic still fits.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The registry's card.</summary>
public static class AcrCard
{
    /// <summary>Basic's included storage, and the pulls a minute it allows, which the dials are read against.</summary>
    public const double BasicGb = 10;
    public const double BasicReadsPerMinute = 1_000;

    /// <summary>What Standard costs a month, for the last row.</summary>
    public const double StandardPerMonth = 20.00;

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
        var dials = new[]
        {
            ResourceView.Dial(inputs, "storage_gb", "Storage", BasicGb, Storage, v => v is { } g ? $"{g.ToString("0.00", CultureInfo.InvariantCulture)} of {BasicGb} GB" : "not read"),
            ResourceView.Dial(inputs, "pulls", "Pulls, this minute", BasicReadsPerMinute, Pulls, v => v is { } p ? $"{p.ToString("0", CultureInfo.InvariantCulture)} of {BasicReadsPerMinute:N0} a minute" : "not read"),
        };
        double? pulls = ResourceView.WindowSum(inputs, "pulls"), ok = ResourceView.WindowSum(inputs, "pulls_ok"), pushes = ResourceView.WindowSum(inputs, "pushes");
        var storageTone = PlanThresholds.ToneOf(ResourceView.Hour(inputs, "storage_gb").Select(v => ResourceView.Share(v, BasicGb)).ToList(), Storage);
        var events = inputs.Hours.SelectMany(h => h.Events).ToList();
        var rows = new List<CloudRow>
        {
            new("holds", "What does it hold?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "repositories")} images, {ResourceView.Fact(inputs, "tags")} tags",
                [new(ResourceView.Fact(inputs, "repository_list"), null)], "good"),
            new("pulled", "Who pulled?", pulls is null ? "not read yet" : $"{pulls.Value.ToString("N0", CultureInfo.InvariantCulture)} pulls",
                [new($"{ResourceView.Number(ok)} succeeded · {ResourceView.Number(pushes)} pushes in the window · the three apps pull with the site's identity", null)],
                pulls is > 0 && ok is { } good && good < pulls ? "warning" : "good"),
            new("stale", "What is stale?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "untagged")} untagged manifests",
                [new($"{ResourceView.Fact(inputs, "untagged_gb")} GB no tag points at · a purge would free it; this card names it and does not run it", null)], "info"),
            new("tier", "Is Basic still right?", inputs.Month.Count == 0 ? "not enough kept yet" : storageTone == PlanTone.Alarm ? "no, storage is past its line" : "yes, storage first",
                [new($"Standard ${StandardPerMonth.ToString("0.00", CultureInfo.InvariantCulture)}/mo with 100 GB · {events.Count} events in the window", null)],
                storageTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "The registry",
            ResourceView.Uptime(inputs),
            $"up, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Storage", $"{ResourceView.Number(ResourceView.Now(inputs, "storage_gb"), "0.00")} / {BasicGb} GB", CloudTones.Of(storageTone)),
                new("Storage p95, 30 d", $"{ResourceView.Number(ResourceFolding.Spread(inputs.Month, "storage_gb").P95, "0.00")} GB", "info"),
                new("Images", ResourceView.Fact(inputs, "repositories"), "info"),
                new("Tags", ResourceView.Fact(inputs, "tags"), "info"),
                new("Untagged", ResourceView.Fact(inputs, "untagged"), "info"),
                new("Untagged size", $"{ResourceView.Fact(inputs, "untagged_gb")} GB", "info"),
                new("Pulls, window", ResourceView.Number(pulls), "info"),
                new("Pulls failed", ResourceView.Number(pulls is { } p && ok is { } o ? p - o : null), pulls is { } p2 && ok is { } o2 && o2 < p2 ? "warning" : "info"),
                new("Pushes, window", ResourceView.Number(pushes), "info"),
                new("Tier", ResourceView.Fact(inputs, "tier"), "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one minute each; the catalogue hourly from the registry");
        return new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, "no minute kept yet; the first is read a minute after the SQL site starts"),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Container Registry",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "storage_gb", "Storage, 60 min", BasicGb), ResourceView.Spark(inputs, "pulls", "Pulls, 60 min", 50)],
            $"Normal: storage amber {Storage.Amber} %, red {Storage.Red} % of 10 GB; pulls amber {Pulls.Amber} %, red {Pulls.Red} % of 1,000 a minute; held 15 min.",
            ResourceView.Chart(inputs, "Storage and pulls", ("storage_gb", "Storage", BasicGb, false), ("pulls", "Pulls", BasicReadsPerMinute, false), Storage.Red, "teal storage · green pulls · gold deploys · red event · dashed 90 %"),
            rows,
            readout);
    }

    /// <summary>The hour's facts from the registry's record and its catalogue.</summary>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement record, AcrHoldings? holdings)
    {
        string? tier = AzureMetrics.Text(record, "sku.name");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tier"] = tier,
            ["login_server"] = AzureMetrics.Text(record, "properties.loginServer"),
            ["repositories"] = holdings?.Repositories.Count.ToString(CultureInfo.InvariantCulture),
            ["tags"] = holdings?.Repositories.Sum(r => r.Tags).ToString(CultureInfo.InvariantCulture),
            ["repository_list"] = holdings is null ? "the catalogue could not be read; AcrPull on the registry is what it needs" : string.Join(" · ", holdings.Repositories.Select(r => $"{r.Name} {r.Tags} tags")),
            ["untagged"] = holdings?.UntaggedManifests.ToString(CultureInfo.InvariantCulture),
            ["untagged_gb"] = holdings is null ? null : (holdings.UntaggedBytes / 1024.0 / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture),
            ["spec"] = string.Join(' ', new[] { tier, "10 GB", AzureMetrics.Text(record, "location") }.Where(p => p is not null)).ToUpperInvariant(),
        };
    }
}
