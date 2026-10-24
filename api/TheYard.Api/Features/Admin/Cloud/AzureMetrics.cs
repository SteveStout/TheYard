// One read of Azure Monitor for any resource the Cloud infrastructure cards follow: the named
// metrics at a minute's grain, and the resource's own record and Activity Log once an hour. The
// plan has a reader of its own (PlanReader.cs) because it also lists its apps; every other
// resource is read through this one, each naming its metrics in its card's definition.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

// #region azure-metrics
/// <summary>One Azure Monitor metric a card keeps: Azure's name for it, how it is aggregated, the name the card keeps it under, and a scale.</summary>
/// <param name="Azure">The metric's name in Azure Monitor.</param>
/// <param name="Aggregation">average, total, maximum or count.</param>
/// <param name="Reading">The name the card keeps it under: lower case, digits and underscores.</param>
/// <param name="Scale">What the value is multiplied by on the way in, to turn bytes into megabytes and the like.</param>
/// <param name="Filter">An Azure Monitor dimension filter, such as StatusCode eq '429'; null for the whole metric.</param>
/// <param name="Interval">The finest grain Azure keeps the metric at: PT1M for most, PT5M or PT1H for a slow one such as a registry's storage.</param>
public sealed record AzureMetric(string Azure, string Aggregation, string Reading, double Scale = 1, string? Filter = null, string Interval = "PT1M");

/// <summary>Reads Azure Monitor and Resource Manager with the site's own identity, for any resource.</summary>
public sealed class AzureMetrics(string clientId, bool configured, HostIdentity? identity = null)
{
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>True only on Azure, where there is an identity to ask with.</summary>
    public bool Configured => configured;

    /// <summary>
    /// The resource's metrics from <paramref name="from"/> to <paramref name="to"/>, one entry per
    /// minute any of them reported. A metric Azure keeps only at five minutes or an hour is read over
    /// its own last grain and its newest value is held on every minute of the read, because it moves
    /// that slowly. A metric Azure refuses is left out rather than failing the others: Azure refuses
    /// a whole request that names one metric or grain the resource does not report.
    /// </summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(string resourceId, IReadOnlyList<AzureMetric> metrics, DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation)
    {
        string token = await Token();
        var series = new Dictionary<string, Dictionary<DateTimeOffset, double>>(StringComparer.Ordinal);
        var held = new Dictionary<string, double>(StringComparer.Ordinal);
        var none = new HashSet<string>(StringComparer.Ordinal);
        // One request per filter and grain, because Azure Monitor applies one filter and one grain to every metric in a request.
        foreach (var group in metrics.GroupBy(metric => (Filter: metric.Filter ?? "", metric.Interval)))
        {
            var start = group.Key.Interval switch { "PT1H" => to.AddHours(-2), "PT5M" => to.AddMinutes(-15), _ => from };
            var read = await ReadGroupAsync(token, resourceId, [.. group], start, to, cancellation);
            foreach (var metric in group)
            {
                if (!read.TryGetValue(metric.Azure, out var points))
                {
                    continue;
                }
                if (points.Count == 0)
                {
                    // Answered with nothing: a count or a total of nothing is zero; an average of nothing stays unread.
                    if (metric.Aggregation is "count" or "total")
                    {
                        none.Add(metric.Reading);
                    }
                    continue;
                }
                // A point stamped at the read's own end is a coarser grain's newest value standing in.
                if (group.Key.Interval == "PT1M" && !(points.Count == 1 && points.ContainsKey(to)))
                {
                    series[metric.Reading] = points.ToDictionary(pair => pair.Key, pair => pair.Value * metric.Scale);
                }
                else
                {
                    held[metric.Reading] = points.MaxBy(pair => pair.Key).Value * metric.Scale;
                }
            }
        }
        var minutes = series.Values.SelectMany(points => points.Keys).Distinct().Order().ToList();
        if (minutes.Count == 0 && (held.Count > 0 || none.Count > 0))
        {
            var utc = to.ToUniversalTime();
            minutes.Add(new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero));
        }
        return minutes.Select(at => new ResourceMinute(
                at,
                metrics.ToDictionary(
                    metric => metric.Reading,
                    metric => series.TryGetValue(metric.Reading, out var points) && points.TryGetValue(at, out double value) ? Math.Round(value, 3)
                        : held.TryGetValue(metric.Reading, out double slow) ? Math.Round(slow, 3)
                        : none.Contains(metric.Reading) ? 0
                        : (double?)null),
                version))
            .ToList();
    }

    /// <summary>One request for a group of metrics; on a refusal, each metric alone, leaving out any Azure refuses.</summary>
    private static async Task<Dictionary<string, Dictionary<DateTimeOffset, double>>> ReadGroupAsync(string token, string resourceId, IReadOnlyList<AzureMetric> group, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string span = $"{from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}/{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        // A filter on a dimension may split the answer into one series a value (CollectionName sw
        // 'tests-' answers one series a test container); top raises Azure's default of ten series.
        string filter = string.IsNullOrEmpty(group[0].Filter) ? "" : "&$filter=" + Uri.EscapeDataString(group[0].Filter!) + "&top=100";
        string Path(IEnumerable<AzureMetric> metrics, string interval, string timespan) =>
            $"{resourceId}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames={string.Join(',', metrics.Select(m => m.Azure).Distinct())}&interval={interval}&timespan={timespan}&aggregation={string.Join(',', metrics.Select(m => m.Aggregation).Distinct())}{filter}";
        try
        {
            using var answer = await GetAsync(token, Path(group, group[0].Interval, span), cancellation);
            var answered = PlanShapes.Series(answer.RootElement, [.. group.Select(m => (m.Azure, m.Aggregation))]);
            // A metric Azure answered for with no points is kept as answered and empty, so a count
            // of nothing reads as zero and not as unread.
            foreach (var metric in group)
            {
                answered.TryAdd(metric.Azure, []);
            }
            return answered;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var read = new Dictionary<string, Dictionary<DateTimeOffset, double>>(StringComparer.Ordinal);
            // Each metric alone, at its own grain and then at the coarser ones Azure offers, so one
            // metric kept only at five minutes or an hour does not cost the others their minute.
            foreach (var metric in group)
            {
                foreach (var (interval, back) in new[] { (metric.Interval, from), ("PT5M", to.AddMinutes(-15)), ("PT1H", to.AddHours(-2)) })
                {
                    try
                    {
                        using var answer = await GetAsync(token, Path([metric], interval, $"{back.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}/{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"), cancellation);
                        var series = PlanShapes.Series(answer.RootElement, [(metric.Azure, metric.Aggregation)]);
                        if (series.TryGetValue(metric.Azure, out var points) && points.Count > 0)
                        {
                            // A coarser grain's newest value stands for the minute, as a slow metric's does.
                            read[metric.Azure] = interval == metric.Interval ? points : new Dictionary<DateTimeOffset, double> { [to] = points.MaxBy(pair => pair.Key).Value };
                        }
                        else
                        {
                            read[metric.Azure] = [];
                        }
                        break;
                    }
                    catch (HttpRequestException one) when (one.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        // Not kept at this grain; the next one is tried, and after the last the card prints it as not read.
                    }
                }
            }
            return read;
        }
    }

    /// <summary>The resource's own record from Resource Manager, for its size and settings, and the tenant for its portal link.</summary>
    public async Task<(JsonDocument Record, string? Tenant)> RecordAsync(string resourceId, string apiVersion, CancellationToken cancellation)
    {
        string token = await Token();
        return (await GetAsync(token, $"{resourceId}?api-version={apiVersion}", cancellation), PlanReader.TenantOf(token));
    }

    /// <summary>The resource's Activity Log between two moments, classified.</summary>
    public async Task<IReadOnlyList<PlanEvent>> EventsAsync(string resourceId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string token = await Token();
        string subscription = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
        string filter = Uri.EscapeDataString($"eventTimestamp ge '{from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}' and eventTimestamp le '{to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}' and resourceUri eq '{resourceId}'");
        using var answer = await GetAsync(token, $"/subscriptions/{subscription}/providers/Microsoft.Insights/eventtypes/management/values?api-version=2015-04-01&$filter={filter}", cancellation);
        return PlanShapes.Events(answer.RootElement, PlanReader.NameOf(resourceId));
    }

    /// <summary>A Resource Manager read of any path under management.azure.com.</summary>
    public async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken cancellation) => await GetAsync(await Token(), pathAndQuery, cancellation);

    /// <summary>
    /// A Kusto query against a Log Analytics workspace through Resource Manager, which needs Log
    /// Analytics Reader on the workspace; the first table's rows come back as text, column by name.
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> QueryAsync(string workspaceId, string kusto, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://management.azure.com{workspaceId}/api/query?api-version=2020-08-01")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = kusto }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await Token());
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return QueryRows(answer.RootElement);
    }

    /// <summary>
    /// The first table of a Log Analytics answer as rows of text by column name; empty when it has
    /// none. The query API answers in camel case (tables, columns, name, rows) and the same query
    /// through Resource Manager in Pascal case (Tables, Columns, ColumnName, Rows), so both are read.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> QueryRows(JsonElement answer)
    {
        if (Property(answer, "tables", "Tables") is not { ValueKind: JsonValueKind.Array } tables || tables.GetArrayLength() == 0)
        {
            return [];
        }
        var table = tables[0];
        if (Property(table, "columns", "Columns") is not { ValueKind: JsonValueKind.Array } columnList || Property(table, "rows", "Rows") is not { ValueKind: JsonValueKind.Array } rowList)
        {
            return [];
        }
        var columns = columnList.EnumerateArray().Select(c => (Property(c, "name", "ColumnName") is { ValueKind: JsonValueKind.String } n ? n.GetString() : null) ?? "").ToList();
        return rowList.EnumerateArray()
            .Select(row => (IReadOnlyDictionary<string, string?>)columns.Select((name, i) => (name, cell: row[i]))
                .ToDictionary(pair => pair.name, pair => pair.cell.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => pair.cell.GetString(),
                    _ => pair.cell.GetRawText(),
                }, StringComparer.Ordinal))
            .ToList();
    }

    private static JsonElement? Property(JsonElement element, string camel, string pascal) =>
        element.ValueKind == JsonValueKind.Object && (element.TryGetProperty(camel, out var found) || element.TryGetProperty(pascal, out found)) ? found : null;

    /// <summary>A Resource Manager token for the site's identity, which the registry exchanges for one of its own.</summary>
    public Task<string> ArmTokenAsync() => Token();

    private Task<string> Token() => IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId, identity ?? HostIdentity.None);

    private static async Task<JsonDocument> GetAsync(string token, string pathAndQuery, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com" + pathAndQuery);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }

    /// <summary>A text property at a dotted path in a Resource Manager record, or null.</summary>
    public static string? Text(JsonElement root, string path)
    {
        var at = root;
        foreach (string part in path.Split('.'))
        {
            if (at.ValueKind != JsonValueKind.Object || !at.TryGetProperty(part, out at))
            {
                return null;
            }
        }
        return at.ValueKind switch
        {
            JsonValueKind.String => at.GetString(),
            JsonValueKind.Number => at.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
}
// #endregion azure-metrics
