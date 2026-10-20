// What the Application Insights card reads, once an hour: the workspace's own Usage table for
// what was ingested today and this month, the effective sampling from the requests' item counts,
// the minutes the daily cap stopped collection, and which apps report. The cap is the workspace's
// daily quota, or the component's when the workspace sets none. All of it through Resource Manager
// with the site's identity, which needs Log Analytics Reader on the workspace.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>Reads the Application Insights component and its Log Analytics workspace.</summary>
public sealed class InsightsSource(AzureMetrics azure, string componentId, string workspaceId)
{
    /// <summary>
    /// The month's free ingestion, which the cost row is read against. Typed, because no record the
    /// workspace returns states its allowance; what the month costs is the bill's own line.
    /// </summary>
    public const double FreeGbPerMonth = 5;

    /// <summary>The queries, kept here so a test can hold their shape.</summary>
    public const string IngestedToday = "Usage | where TimeGenerated >= startofday(now()) and IsBillable | summarize gb = sum(Quantity) / 1024.0";
    public const string IngestedMonth = "Usage | where TimeGenerated >= startofmonth(now()) and IsBillable | summarize gb = sum(Quantity) / 1024.0";
    public const string Sampling = "AppRequests | where TimeGenerated > ago(1h) | summarize kept = count(), seen = sum(ItemCount)";
    public const string Capped = "_LogOperation | where TimeGenerated > ago(1h) and Operation has 'Data collection stopped' | summarize capped = count()";
    public const string Reporters = "union AppRequests, AppTraces, AppDependencies | where TimeGenerated > ago(1d) | summarize items = count() by AppRoleName | order by items desc";

    /// <summary>The hour's one reading.</summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset now, string version, CancellationToken cancellation)
    {
        double? today = await NumberAsync(IngestedToday, "gb", cancellation);
        double? month = await NumberAsync(IngestedMonth, "gb", cancellation);
        var sampled = (await RowsAsync(Sampling, cancellation)).FirstOrDefault();
        double? kept = Parse(sampled?.GetValueOrDefault("kept")), seen = Parse(sampled?.GetValueOrDefault("seen"));
        double? capped = await NumberAsync(Capped, "capped", cancellation);
        var values = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["ingest_today_gb"] = today is { } t ? Math.Round(t, 4) : null,
            ["ingest_month_gb"] = month is { } m ? Math.Round(m, 4) : null,
            ["sampling"] = kept is { } k && seen is > 0 ? Math.Round(100 * k / seen.Value, 1) : null,
            ["capped"] = capped,
        };
        return [new ResourceMinute(MachineWindows.BucketOf(now, MachineGrain.Hour), values, version)];
    }

    /// <summary>The hour's facts: the cap, the component's own sampling setting, who reports, and the Activity Log.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var (component, tenant) = await azure.RecordAsync(componentId, "2020-02-02", cancellation);
        var (workspace, _) = await azure.RecordAsync(workspaceId, "2023-09-01", cancellation);
        using var _c = component;
        using var _w = workspace;
        double? workspaceCap = Parse(AzureMetrics.Text(workspace.RootElement, "properties.workspaceCapping.dailyQuotaGb"));
        double? componentCap = null;
        try
        {
            using var billing = await azure.GetJsonAsync($"{componentId}/currentbillingfeatures?api-version=2015-05-01", cancellation);
            componentCap = Parse(AzureMetrics.Text(billing.RootElement, "DataVolumeCap.Cap"));
        }
        catch (HttpRequestException)
        {
            // A component that does not answer its billing features leaves the workspace's cap to speak.
        }
        double? cap = workspaceCap is > 0 ? workspaceCap : componentCap;
        var reporters = await RowsAsync(Reporters, cancellation);
        var events = (await azure.EventsAsync(componentId, since, hour, cancellation)).Concat(await azure.EventsAsync(workspaceId, since, hour, cancellation)).ToList();
        return new ResourceHour(hour, componentId, tenant, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cap_gb"] = cap?.ToString("0.###", CultureInfo.InvariantCulture),
            ["cap_from"] = workspaceCap is > 0 ? "the workspace" : componentCap is not null ? "the component" : null,
            ["sampling_setting"] = AzureMetrics.Text(component.RootElement, "properties.SamplingPercentage") ?? "100",
            ["retention_days"] = AzureMetrics.Text(workspace.RootElement, "properties.retentionInDays"),
            ["reporters"] = reporters.Count == 0 ? "nothing reported in a day" : string.Join(" · ", reporters.Select(r => $"{r.GetValueOrDefault("AppRoleName") ?? "unnamed"} {r.GetValueOrDefault("items")}")),
            ["reporter_rows"] = CloudTable.ToFact(reporters.Select(r => new[] { r.GetValueOrDefault("AppRoleName") ?? "unnamed", "Web app", r.GetValueOrDefault("items") })),
            ["reporter_count"] = reporters.Count.ToString(CultureInfo.InvariantCulture),
            ["workspace_name"] = PlanReader.NameOf(workspaceId),
            ["workspace_href"] = PlanView.Portal(tenant, workspaceId),
            ["query"] = _refused ? "refused" : "read",
            ["spec"] = $"PAY AS YOU GO · {AzureMetrics.Text(component.RootElement, "location")?.ToUpperInvariant()}",
        }, events);
    }

    private async Task<double?> NumberAsync(string kusto, string column, CancellationToken cancellation) =>
        Parse((await RowsAsync(kusto, cancellation)).FirstOrDefault()?.GetValueOrDefault(column));

    /// <summary>True when the workspace refused a query: the identity has Reader, which cannot run one.</summary>
    private bool _refused;

    /// <summary>A query's rows; empty, and remembered as refused, when the workspace answers 403.</summary>
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> RowsAsync(string kusto, CancellationToken cancellation)
    {
        try
        {
            return await azure.QueryAsync(workspaceId, kusto, cancellation);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _refused = true;
            return [];
        }
    }

    /// <summary>A number from a query cell or a record, or null.</summary>
    public static double? Parse(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
}
