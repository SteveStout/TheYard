// The App Service plan, read from Azure: one minute of Azure Monitor's readings for the plan and
// for each app on it, and once an hour the plan's size, its apps' states and its Activity Log.
// Its own file because asking Azure (the token, the paths, the shapes it answers in) is a
// different job from keeping the answer, which is PlanRecorder.cs, and shaping it for the card,
// which is PlanReport.cs.
using System.Globalization;
using System.Text;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

// #region plan-reader
/// <summary>
/// Reads the plan with the site's own managed identity, which needs Reader on the plan and on
/// each app (ADR: The App Service plan, read every minute). No key anywhere. The plan's id is not
/// configured: it is read off this site, so the reader follows the site if it ever moves plans.
/// </summary>
public sealed class PlanReader(string clientId, string selfResourceId, bool configured)
{
    private static HttpClient Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>The plan's metrics, with the aggregation each is read with.</summary>
    public static readonly (string Name, string Aggregation)[] PlanMetrics =
    [
        ("CpuPercentage", "average"), ("MemoryPercentage", "average"), ("DiskQueueLength", "average"),
        ("HttpQueueLength", "average"), ("BytesSent", "total"), ("BytesReceived", "total"), ("SocketOutboundAll", "average"),
    ];

    /// <summary>Each app's metrics, with the aggregation each is read with.</summary>
    public static readonly (string Name, string Aggregation)[] AppMetrics =
    [
        ("CpuTime", "total"), ("MemoryWorkingSet", "average"), ("Requests", "total"), ("Http5xx", "total"),
    ];

    /// <summary>
    /// The app's storage, which Azure keeps only at six hours: asked apart at that grain and its
    /// newest value held on every minute, because a request at a minute's grain that names it is
    /// refused whole.
    /// </summary>
    public static readonly (string Name, string Aggregation) FileSystem = ("FileSystemUsage", "average");

    /// <summary>True only on Azure, where there is an identity to ask with and a site to start from.</summary>
    public bool Configured => configured && !string.IsNullOrWhiteSpace(selfResourceId);

    /// <summary>The plan's resource name once it has been read off this site; null before the first read.</summary>
    public string? PlanName => _layout is { } known ? NameOf(known.PlanId) : null;

    /// <summary>The plan's id and the ids of every app on it, read off this site and the plan, cached for an hour.</summary>
    private (DateTimeOffset At, string PlanId, IReadOnlyList<string> Apps)? _layout;

    /// <summary>
    /// The minutes Azure Monitor has for the plan and its apps from <paramref name="from"/> to
    /// <paramref name="to"/>, one entry per minute; a figure Azure did not report is null.
    /// </summary>
    public async Task<IReadOnlyList<PlanMinute>> ReadMinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId);
        var (planId, apps) = await LayoutAsync(token, cancellation);
        var plan = await MetricsAsync(token, planId, PlanMetrics, from, to, cancellation);
        var perApp = new List<(string Name, Dictionary<string, Dictionary<DateTimeOffset, double>> Series)>();
        var storage = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (string app in apps)
        {
            perApp.Add((NameOf(app), await MetricsAsync(token, app, AppMetrics, from, to, cancellation)));
            storage[NameOf(app)] = await StorageAsync(token, app, to, cancellation);
        }

        var minutes = plan.Values.SelectMany(series => series.Keys).Distinct().Order().ToList();
        return minutes.Select(at => new PlanMinute(
                at,
                Value(plan, "CpuPercentage", at),
                Value(plan, "MemoryPercentage", at),
                Value(plan, "DiskQueueLength", at),
                Value(plan, "HttpQueueLength", at),
                Value(plan, "BytesSent", at),
                Value(plan, "BytesReceived", at),
                Value(plan, "SocketOutboundAll", at),
                version,
                perApp.Select(app => new PlanAppMinute(
                    app.Name,
                    Value(app.Series, "CpuTime", at),
                    Value(app.Series, "MemoryWorkingSet", at) is { } bytes ? Math.Round(bytes / 1024 / 1024, 1) : null,
                    Value(app.Series, "Requests", at),
                    Value(app.Series, "Http5xx", at),
                    // AppConnections is a Windows plan's metric; Azure refuses the whole read on a Linux plan.
                    null,
                    storage.GetValueOrDefault(app.Name) is { } used ? Math.Round(used / 1024 / 1024, 1) : null)).ToList()))
            .ToList();
    }

    /// <summary>The hour's slower read: the plan's size, each app's state and configuration, and the Activity Log since <paramref name="since"/>.</summary>
    public async Task<PlanHour> ReadHourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId);
        var (planId, apps) = await LayoutAsync(token, cancellation, fresh: true);
        using var plan = await GetAsync(token, planId + "?api-version=2023-12-01", cancellation);
        var sku = plan.RootElement.TryGetProperty("sku", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        string? skuName = sku.ValueKind == JsonValueKind.Object && sku.TryGetProperty("name", out var n) ? n.GetString() : null;
        int? instances = sku.ValueKind == JsonValueKind.Object && sku.TryGetProperty("capacity", out var c) && c.TryGetInt32(out int count) ? count : null;
        var states = new List<PlanAppState>();
        foreach (string app in apps)
        {
            using var site = await GetAsync(token, app + "?api-version=2023-12-01", cancellation);
            var props = site.RootElement.GetProperty("properties");
            bool? alwaysOn = props.TryGetProperty("siteConfig", out var config) && config.TryGetProperty("alwaysOn", out var on) && on.ValueKind is JsonValueKind.True or JsonValueKind.False ? on.GetBoolean() : null;
            DateTimeOffset? modified = props.TryGetProperty("lastModifiedTimeUtc", out var lm) && DateTimeOffset.TryParse(lm.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            states.Add(new PlanAppState(NameOf(app), app, props.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown", alwaysOn, modified));
        }

        var events = new List<PlanEvent>();
        foreach (string resource in apps.Prepend(planId))
        {
            events.AddRange(await EventsAsync(token, resource, since, hour.AddHours(1), cancellation));
        }

        return new PlanHour(
            hour,
            planId,
            TenantOf(token),
            skuName,
            instances,
            PlanSizes.CoresOf(skuName),
            PlanSizes.MemoryGbOf(skuName),
            states,
            events.OrderBy(e => e.At).ToList(),
            plan.RootElement.TryGetProperty("location", out var where) ? where.GetString() : null,
            plan.RootElement.TryGetProperty("kind", out var kind) ? kind.GetString()?.Contains("linux", StringComparison.OrdinalIgnoreCase) : null);
    }
    // #endregion plan-reader

    /// <summary>This site's plan and the apps on it, read once an hour or when asked fresh.</summary>
    private async Task<(string PlanId, IReadOnlyList<string> Apps)> LayoutAsync(string token, CancellationToken cancellation, bool fresh = false)
    {
        if (!fresh && _layout is { } known && DateTimeOffset.UtcNow - known.At < TimeSpan.FromHours(1))
        {
            return (known.PlanId, known.Apps);
        }
        using var site = await GetAsync(token, selfResourceId + "?api-version=2023-12-01", cancellation);
        string planId = site.RootElement.GetProperty("properties").GetProperty("serverFarmId").GetString()!;
        using var listed = await GetAsync(token, planId + "/sites?api-version=2023-12-01", cancellation);
        var apps = listed.RootElement.GetProperty("value").EnumerateArray()
            .Select(app => app.GetProperty("id").GetString()!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _layout = (DateTimeOffset.UtcNow, planId, apps);
        return (planId, apps);
    }

    /// <summary>One resource's metrics at a minute's grain, as metric name to minute to value.</summary>
    private static async Task<Dictionary<string, Dictionary<DateTimeOffset, double>>> MetricsAsync(
        string token, string resourceId, (string Name, string Aggregation)[] metrics, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string names = string.Join(',', metrics.Select(metric => metric.Name));
        string aggregations = string.Join(',', metrics.Select(metric => metric.Aggregation).Distinct());
        string span = $"{from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}/{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        using var answer = await GetAsync(token, $"{resourceId}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames={names}&interval=PT1M&timespan={span}&aggregation={aggregations}", cancellation);
        return PlanShapes.Series(answer.RootElement, metrics);
    }

    /// <summary>An app's newest storage reading at its six-hour grain, or null when Azure has none or refuses.</summary>
    private static async Task<double?> StorageAsync(string token, string app, DateTimeOffset to, CancellationToken cancellation)
    {
        string span = $"{to.AddHours(-12).UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}/{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        try
        {
            using var answer = await GetAsync(token, $"{app}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames={FileSystem.Name}&interval=PT6H&timespan={span}&aggregation={FileSystem.Aggregation}", cancellation);
            return PlanShapes.Series(answer.RootElement, [FileSystem]).TryGetValue(FileSystem.Name, out var points) && points.Count > 0
                ? points.MaxBy(pair => pair.Key).Value
                : null;
        }
        catch (HttpRequestException)
        {
            // Storage is one figure on the card; a refused read of it leaves the minute whole.
            return null;
        }
    }

    /// <summary>The Activity Log entries for one resource in a span, classified.</summary>
    private static async Task<IReadOnlyList<PlanEvent>> EventsAsync(string token, string resourceId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string subscription = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
        string filter = Uri.EscapeDataString($"eventTimestamp ge '{from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}' and eventTimestamp le '{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}' and resourceUri eq '{resourceId}'");
        using var answer = await GetAsync(token, $"/subscriptions/{subscription}/providers/Microsoft.Insights/eventtypes/management/values?api-version=2015-04-01&$filter={filter}", cancellation);
        return PlanShapes.Events(answer.RootElement, NameOf(resourceId));
    }

    private static async Task<JsonDocument> GetAsync(string token, string pathAndQuery, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com" + pathAndQuery);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    private static double? Value(Dictionary<string, Dictionary<DateTimeOffset, double>> series, string metric, DateTimeOffset at) =>
        series.TryGetValue(metric, out var points) && points.TryGetValue(at, out double value) ? Math.Round(value, 2) : null;

    /// <summary>The last segment of a resource id: the name the portal shows.</summary>
    public static string NameOf(string resourceId) => resourceId.TrimEnd('/').Split('/')[^1];

    /// <summary>
    /// The tenant the token was issued in, read from its own claims, so the portal links need no
    /// tenant in configuration. Null when the token cannot be read.
    /// </summary>
    public static string? TenantOf(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }
        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return claims.RootElement.TryGetProperty("tid", out var tid) ? tid.GetString() : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
