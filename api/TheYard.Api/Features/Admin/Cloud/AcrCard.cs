// The Azure Container Registry card (ADR: One frame for every resource): storage against Basic's
// 10 GB and pulls a minute on the dials, the images and tags it holds, the untagged layers it is
// still paying to keep, named and never purged, and whether Basic still fits.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

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

    /// <summary>
    /// What each image is, in a sentence, by repository name: no record states it, so it is typed
    /// here and only here, read against deploy.yml (the API's image, built by Dockerfile with The Shed
    /// published inside it, serving both sites from one web app) and deploy-render.yml (the
    /// renderer's image, built by render/Dockerfile, a Node process running render/server.mjs). An
    /// image this map does not name shows "no description", and the row warns.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["theyard"] = ".NET API serving TheYard, TheYard on Cosmos and The Shed from one web app",
        ["theyard-render"] = "Node.js server that renders the React pages before they reach the browser",
    };

    /// <summary>
    /// The two images that ship one build: the renderer draws the pages of the API's build, and
    /// deploy-render.yml can land after deploy.yml, so the row warns while their live tags differ.
    /// </summary>
    public static readonly (string Api, string Renderer) SameBuild = ("theyard", "theyard-render");

    /// <summary>The image table's columns.</summary>
    public static readonly IReadOnlyList<string> ImageColumns = ["Image", "What it is", "Runs on", "Live tag", "Tags · untagged", "Size", "Last push"];

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
            Holds(inputs),
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
    /// <param name="plan">The plan card's newest kept hour, the one read of each web app's image; null when none is kept.</param>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement record, AcrHoldings? holdings, System.Text.Json.JsonElement? usages = null, PlanHour? plan = null)
    {
        string? tier = AzureMetrics.Text(record, "sku.name");
        string? loginServer = AzureMetrics.Text(record, "properties.loginServer");
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tier"] = tier,
            ["size_limit_gb"] = SizeLimitGb(usages)?.ToString("0.##", CultureInfo.InvariantCulture),
            ["login_server"] = AzureMetrics.Text(record, "properties.loginServer"),
            ["repositories"] = holdings?.Images.Count.ToString(CultureInfo.InvariantCulture),
            ["tags"] = holdings?.Images.Sum(r => r.Tags).ToString(CultureInfo.InvariantCulture),
            ["image_rows"] = holdings is null ? null : ImageRows(holdings, loginServer, plan),
            ["apps_read"] = plan is null ? null : plan.Apps.Count.ToString(CultureInfo.InvariantCulture),
            ["repository_list"] = holdings is null ? "the catalogue could not be read; AcrPull on the registry is what it needs" : string.Join(" · ", holdings.Images.Select(r => $"{r.Name} {r.Tags} tags")),
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

    // #region image-table
    /// <summary>
    /// The holds row: one table row an image, what it is from the typed map, which web apps run it
    /// and at which tag from the plan card's own hour, its tags, its bytes and its newest push. Warns
    /// when an image has no description or the two images of one build run different tags.
    /// </summary>
    public static CloudRow Holds(ResourceCardInputs inputs)
    {
        if (inputs.Hours.Count == 0)
        {
            return new CloudRow("holds", "What does it hold?", "not read yet", [], "info");
        }
        string answer = $"{ResourceView.Fact(inputs, "repositories")} images, {ResourceView.Fact(inputs, "tags")} tags";
        string? fact = ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("image_rows");
        if (fact is null)
        {
            return new CloudRow("holds", "What does it hold?", answer, [new(ResourceView.Fact(inputs, "repository_list"), null)], "info");
        }
        var images = fact.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).Where(cells => cells.Length >= 7).ToList();
        var faults = new List<CloudLink>();
        // A missing description is a line for the developer, information, never a fault on the card.
        var notes = new List<CloudLink>();
        var rows = new List<IReadOnlyList<CloudLink>>();
        var live = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cells in images)
        {
            string name = cells[0];
            var apps = cells[6].Split('\u001E', StringSplitOptions.RemoveEmptyEntries).Select(app => app.Split('\u001F')).Where(app => app.Length == 3).ToList();
            string tags = string.Join(", ", apps.Select(app => app[2]).Where(tag => tag.Length > 0).Distinct(StringComparer.Ordinal));
            live[name] = tags;
            bool described = Descriptions.TryGetValue(name, out var what);
            if (!described)
            {
                notes.Add(new($"{name} has no description: say what it is in AcrCard.Descriptions", null));
            }
            CloudLink runsOn = apps.Count == 0 ? new("no web app runs it", null)
                : new(string.Join(", ", apps.Select(app => app[0])), apps[0][1].Length > 0 ? apps[0][1] : null);
            rows.Add(
            [
                new(name, null),
                new(described ? what! : "no description", null),
                runsOn,
                new(tags.Length > 0 ? tags : "none", null),
                new($"{cells[1]} · {cells[2]} untagged", null),
                new($"{Gb(cells[3])} GB, {Gb(cells[4])} GB untagged", null),
                new(DateTimeOffset.TryParse(cells[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var pushed)
                    ? TheYard.Domain.Features.Admin.NightlySlots.Local(pushed).ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " Central" : "not read", null),
            ]);
        }
        if (live.TryGetValue(SameBuild.Api, out var apiTag) && live.TryGetValue(SameBuild.Renderer, out var renderTag)
            && apiTag.Length > 0 && renderTag.Length > 0 && apiTag != renderTag)
        {
            faults.Add(new($"{SameBuild.Api} runs {apiTag} and {SameBuild.Renderer} runs {renderTag}: until they match, pages are drawn in the browser", null));
        }
        IReadOnlyList<CloudLink> detail = faults.Count + notes.Count > 0 ? [.. faults, .. notes]
            : ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("apps_read") is null ? [new CloudLink("which web app runs each image is read from the plan card's hour; none is kept yet", null)]
            : [];
        // The table is the hour's catalogue, so it says its own age beside the per-minute live read.
        detail = [.. detail, new CloudLink(CatalogueAge(inputs), null)];
        string tone = faults.Count > 0 ? "warning" : notes.Count > 0 ? "info" : "good";
        return new CloudRow("holds", "What does it hold?", answer, detail, tone) { Table = new CloudTable(ImageColumns, rows) };
    }

    /// <summary>How old the hour's catalogue is, in whole minutes.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static string CatalogueAge(ResourceCardInputs inputs)
    {
        var at = ResourceView.Latest(inputs)?.At;
        return at is { } read ? $"the table is the hourly catalogue, read {Math.Max(0, (int)(inputs.Now - read).TotalMinutes)} min ago" : "the table is the hourly catalogue, not read yet";
    }

    /// <summary>Bytes as gigabytes to two places.</summary>
    private static string Gb(string bytes) =>
        long.TryParse(bytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out long b) ? (b / 1024.0 / 1024 / 1024).ToString("0.00", CultureInfo.InvariantCulture) : "not read";

    /// <summary>The image rows as a fact, one line an image, with each web app that runs it as name, portal link and live tag.</summary>
    /// <param name="holdings">The registry's catalogue.</param>
    /// <param name="loginServer">The registry's login server, which a web app's image must name to be this registry's.</param>
    /// <param name="plan">The plan card's newest hour, whose apps carry their linuxFxVersion; null when none is kept.</param>
    public static string ImageRows(AcrHoldings holdings, string? loginServer, PlanHour? plan) =>
        CloudTable.ToFact(holdings.Images.Select(image => new[]
        {
            image.Name,
            image.Tags.ToString(CultureInfo.InvariantCulture),
            image.Untagged.ToString(CultureInfo.InvariantCulture),
            image.Bytes.ToString(CultureInfo.InvariantCulture),
            image.UntaggedBytes.ToString(CultureInfo.InvariantCulture),
            image.LastPush?.ToString("O", CultureInfo.InvariantCulture),
            string.Join('\u001E', (plan?.Apps ?? Array.Empty<PlanAppState>()).Select(app => (app, image: ImageOf(app.Image)))
                .Where(pair => pair.image is { } named && named.Repository == image.Name && (loginServer is null || string.Equals(named.Host, loginServer, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => $"{pair.app.Name}\u001F{PlanView.Portal(plan!.TenantId, pair.app.ResourceId)}\u001F{pair.image!.Value.Tag}")),
        }));

    /// <summary>A web app's linuxFxVersion as host, repository and tag: "DOCKER|cr.azurecr.io/theyard:v1" is (cr.azurecr.io, theyard, v1); null when it names no image.</summary>
    /// <param name="linuxFxVersion">The app's setting, as the plan card's hour kept it.</param>
    public static (string Host, string Repository, string Tag)? ImageOf(string? linuxFxVersion)
    {
        if (string.IsNullOrWhiteSpace(linuxFxVersion))
        {
            return null;
        }
        string reference = linuxFxVersion[(linuxFxVersion.IndexOf('|', StringComparison.Ordinal) + 1)..].Trim();
        int slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return null;
        }
        string path = reference[(slash + 1)..];
        int colon = path.LastIndexOf(':');
        return (reference[..slash], colon < 0 ? path : path[..colon], colon < 0 ? "" : path[(colon + 1)..]);
    }
    // #endregion image-table
}
