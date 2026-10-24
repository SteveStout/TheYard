// What the two storage cards read once an hour (ADR: One frame for every resource, the addendum on the
// storage cards): the job-hunt share's record, its transactions by API, by authentication and by hour,
// the alert rule's own line, and the account's days on the bill; the blob service's record, its
// containers and what each holds, and its reads by hour. Every read is one request to Azure Resource
// Manager, Azure Monitor or the blob service's own listing, through the host's outbound client; a read
// that is refused leaves its fact out, and the card prints the gap as a sentence.
using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

// #region storage-source
/// <summary>The hourly reads behind the share card and the blob card, over the storage account named in the nightly settings.</summary>
/// <param name="azure">The Azure Monitor and Resource Manager reader.</param>
/// <param name="directory">The list of the job hunt's resource group, where the account is found.</param>
/// <param name="names">The nightly names: the group and the storage account.</param>
/// <param name="costs">The bill's kept days, the account's line read from them.</param>
/// <param name="blobs">The blob service's listing, through the site's identity.</param>
/// <param name="shareName">The share the card is about.</param>
/// <param name="alertName">The metric alert rule on the share's transactions, whose threshold the card reads.</param>
public sealed class StorageSource(AzureMetrics azure, ResourceDirectory directory, NightlyResources names, ICostHistory costs, BlobListing blobs, string shareName = "jobhunt", string alertName = "alert-jobhunt-share-transactions")
{
    /// <summary>The storage account's Resource Manager type.</summary>
    public const string AccountType = "Microsoft.Storage/storageAccounts";

    /// <summary>The account's names, for the card's portal links and its bill line.</summary>
    public NightlyResources Names => names;

    /// <summary>The account's resource, or null when the group does not hold it.</summary>
    public async Task<AzureResource?> AccountAsync(CancellationToken cancellation) =>
        azure.Configured ? ResourceDirectory.Find(await directory.AllAsync(cancellation), AccountType, names.Storage) : null;

    /// <summary>The minutes of a service under the account: fileServices or blobServices.</summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(string service, IReadOnlyList<AzureMetric> metrics, DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation) =>
        await AccountAsync(cancellation) is { } account ? await azure.MinutesAsync($"{account.Id}/{service}/default", metrics, from, to, version, cancellation) : [];

    /// <summary>The share card's hour: the share's record, the alert's line, the transactions by API, authentication and hour, and the bill.</summary>
    public async Task<ResourceHour> ShareHourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []);
        }
        if (await AccountAsync(cancellation) is not { } account)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?> { [ResourceView.MissingFact] = ResourceDirectory.Missing(names.Storage) }, []);
        }
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var (record, tenant) = await azure.RecordAsync(account.Id, "2023-05-01", cancellation);
        using (record)
        {
            facts["sku"] = AzureMetrics.Text(record.RootElement, "sku.name");
        }
        string service = $"{account.Id}/fileServices/default";
        await Try(facts, "share", async () =>
        {
            using var share = await azure.GetJsonAsync($"{service}/shares/{shareName}?api-version=2023-05-01&$expand=stats", cancellation);
            ShareCard.ShareFacts(facts, share.RootElement);
        });
        await Try(facts, "alert", async () =>
        {
            using var rule = await azure.GetJsonAsync($"{names.GroupPath(account.Id.Split('/')[2])}/providers/Microsoft.Insights/metricAlerts/{alertName}?api-version=2018-03-01", cancellation);
            facts["alert_threshold"] = Threshold(rule.RootElement);
            facts["alert_enabled"] = AzureMetrics.Text(rule.RootElement, "properties.enabled");
        });
        var dayStart = new DateTimeOffset(hour.UtcDateTime.Date, TimeSpan.Zero);
        await Try(facts, "api", async () =>
        {
            var byApi = await SplitAsync(service, "ApiName", dayStart.AddDays(-1), hour.AddHours(1), cancellation);
            facts["api_rows"] = ShareCard.ApiRows(byApi, dayStart, hour);
        });
        await Try(facts, "auth", async () =>
        {
            var byAuth = await SplitAsync(service, "Authentication", hour.AddHours(-1), hour.AddHours(1), cancellation);
            facts["auth_hour"] = string.Join(';', byAuth.Select(pair => $"{pair.Key}={pair.Value.Where(p => p.Key >= hour.AddHours(-1)).Sum(p => p.Value).ToString("0", CultureInfo.InvariantCulture)}"));
        });
        await Try(facts, "hourly", async () =>
        {
            facts["tx_hourly"] = Series.Encode(await TotalsAsync(service, "Transactions", null, hour.AddDays(-30), hour.AddHours(1), cancellation));
            facts["listings_hourly"] = Series.Encode(await TotalsAsync(service, "Transactions", ShareCard.ListingFilter, hour.AddDays(-7), hour.AddHours(1), cancellation));
        });
        await Try(facts, "diagnostics", async () =>
        {
            using var settings = await azure.GetJsonAsync($"{service}/providers/Microsoft.Insights/diagnosticSettings?api-version=2021-05-01-preview", cancellation);
            facts["diagnostic_log"] = settings.RootElement.TryGetProperty("value", out var list) && list.GetArrayLength() > 0 ? "on" : "off";
        });
        await Try(facts, "bill", async () => ShareCard.CostFacts(facts, await costs.ReadAsync(hour.AddDays(-34).UtcDay, cancellation), names.Storage, hour));
        return new ResourceHour(hour, $"{service}/shares/{shareName}", tenant, facts, await azure.EventsAsync(account.Id, since, hour, cancellation));
    }

    /// <summary>The blob card's hour: the account's record, the containers and what each holds, and the reads by hour.</summary>
    public async Task<ResourceHour> BlobHourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []);
        }
        if (await AccountAsync(cancellation) is not { } account)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?> { [ResourceView.MissingFact] = ResourceDirectory.Missing(names.Storage) }, []);
        }
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var (record, tenant) = await azure.RecordAsync(account.Id, "2023-05-01", cancellation);
        using (record)
        {
            BlobCard.AccountFacts(facts, record.RootElement);
        }
        string service = $"{account.Id}/blobServices/default";
        var containers = new List<(string Name, string Access, string? Purpose)>();
        await Try(facts, "containers", async () =>
        {
            using var list = await azure.GetJsonAsync($"{service}/containers?api-version=2023-05-01", cancellation);
            containers = BlobCard.Containers(list.RootElement);
        });
        await Try(facts, "lifecycle", async () =>
        {
            try
            {
                using var policy = await azure.GetJsonAsync($"{account.Id}/managementPolicies/default?api-version=2023-05-01", cancellation);
                facts["lifecycle"] = policy.RootElement.TryGetProperty("properties", out var p) && p.TryGetProperty("policy", out var pol) && pol.TryGetProperty("rules", out var rules) ? $"{rules.GetArrayLength()} rules" : "none yet";
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                facts["lifecycle"] = "none yet";
            }
        });
        var listed = await blobs.ListAsync(names.Storage, [.. containers.Select(c => c.Name)], cancellation);
        BlobCard.ListingFacts(facts, containers, listed, blobs.Last);
        await Try(facts, "hourly", async () =>
        {
            facts["reads_hourly"] = Series.Encode(await TotalsAsync(service, "Transactions", BlobCard.ReadFilter, hour.AddDays(-30), hour.AddHours(1), cancellation));
            var monthStart = new DateTimeOffset(hour.Year, hour.Month, 1, 0, 0, 0, TimeSpan.Zero);
            facts["egress_month_bytes"] = (await TotalsAsync(service, "Egress", null, monthStart, hour.AddHours(1), cancellation)).Values.Sum().ToString("0", CultureInfo.InvariantCulture);
        });
        await Try(facts, "bill", async () => ShareCard.CostFacts(facts, await costs.ReadAsync(hour.AddDays(-34).UtcDay, cancellation), names.Storage, hour));
        return new ResourceHour(hour, service, tenant, facts, await azure.EventsAsync(account.Id, since, hour, cancellation));
    }

    /// <summary>A metric's hourly totals over a span, summed across any split a filter makes.</summary>
    private async Task<IReadOnlyDictionary<DateTimeOffset, double>> TotalsAsync(string resourceId, string metric, string? filter, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string span = $"{from.UtcDateTime:yyyy-MM-ddTHH:00:00Z}/{to.UtcDateTime:yyyy-MM-ddTHH:00:00Z}";
        string where = filter is null ? "" : "&$filter=" + Uri.EscapeDataString(filter) + "&top=100";
        using var answer = await azure.GetJsonAsync($"{resourceId}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames={metric}&interval=PT1H&timespan={span}&aggregation=total{where}", cancellation);
        return PlanShapes.Series(answer.RootElement, [(metric, "total")]).GetValueOrDefault(metric) ?? [];
    }

    /// <summary>The share's transactions an hour, one series a value of the dimension.</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DateTimeOffset, double>>> SplitAsync(string resourceId, string dimension, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        string span = $"{from.UtcDateTime:yyyy-MM-ddTHH:00:00Z}/{to.UtcDateTime:yyyy-MM-ddTHH:00:00Z}";
        string filter = Uri.EscapeDataString($"{dimension} eq '*'");
        using var answer = await azure.GetJsonAsync($"{resourceId}/providers/microsoft.insights/metrics?api-version=2023-10-01&metricnames=Transactions&interval=PT1H&timespan={span}&aggregation=total&$filter={filter}&top=100", cancellation);
        return Series.Split(answer.RootElement);
    }

    /// <summary>The threshold of a rule whose criteria the dotted path cannot index.</summary>
    private static string? Threshold(JsonElement rule) =>
        rule.TryGetProperty("properties", out var p) && p.TryGetProperty("criteria", out var c) && c.TryGetProperty("allOf", out var all) && all.GetArrayLength() > 0
            && all[0].TryGetProperty("threshold", out var t) ? t.GetRawText() : null;

    /// <summary>One read that may be refused: a refusal is kept as a fact naming what was not read, and the hour goes on.</summary>
    private static async Task Try(Dictionary<string, string?> facts, string what, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            facts[$"refused_{what}"] = ex is HttpRequestException http && http.StatusCode is { } status ? ((int)status).ToString(CultureInfo.InvariantCulture) : ex.GetType().Name;
        }
    }
}
// #endregion storage-source
