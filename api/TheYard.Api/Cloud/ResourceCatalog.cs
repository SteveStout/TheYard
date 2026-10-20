// The six cards' definitions in one list: which resource each finds in the group, which metrics
// it reads a minute, what it reads an hour, and which view draws it. Off Azure every card reads
// nothing and says so, because there is no identity to ask with.
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The six resource cards, in the rail's order.</summary>
public static class ResourceCatalog
{
    /// <summary>Resource Manager types and api versions the cards read.</summary>
    public const string SqlType = "Microsoft.Sql/servers/databases", CosmosType = "Microsoft.DocumentDB/databaseAccounts",
        AcrType = "Microsoft.ContainerRegistry/registries", InsightsType = "Microsoft.Insights/components",
        WorkspaceType = "Microsoft.OperationalInsights/workspaces", MailType = "Microsoft.Communication/CommunicationServices";

    /// <summary>The database the SQL card reads; any other user database on the server is listed from its own record.</summary>
    public const string SqlDatabase = "sqldb-theyard-ss-basic";

    /// <param name="sqlSite">The public name whose requests get the SQL database and the web app that answers it, from this process's own settings; null where it serves none.</param>
    public static IReadOnlyList<ResourceCard> Cards(AzureMetrics azure, ResourceDirectory directory, NetlifyEdge edge, (int Tables, string Migration) model, string? sqlSite = null)
    {
        static IReadOnlyList<string> ReadingsOf(IReadOnlyList<AzureMetric> metrics) => [.. metrics.Select(m => m.Reading).Distinct()];

        Func<DateTimeOffset, DateTimeOffset, string, CancellationToken, Task<IReadOnlyList<ResourceMinute>>> Monitor(string type, string? name, IReadOnlyList<AzureMetric> metrics) =>
            async (from, to, version, cancellation) => azure.Configured
                ? await azure.MinutesAsync((await directory.FindAsync(type, name, cancellation)).Id, metrics, from, to, version, cancellation)
                : [];

        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<ResourceHour>> Hourly(string type, string? name, string apiVersion, Func<AzureResource, JsonElement, CancellationToken, Task<IReadOnlyDictionary<string, string?>>> facts) =>
            async (hour, since, cancellation) =>
            {
                if (!azure.Configured)
                {
                    return new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []);
                }
                var resource = await directory.FindAsync(type, name, cancellation);
                var (record, tenant) = await azure.RecordAsync(resource.Id, apiVersion, cancellation);
                using (record)
                {
                    return new ResourceHour(hour, resource.Id, tenant, await facts(resource, record.RootElement, cancellation), await azure.EventsAsync(resource.Id, since, hour, cancellation));
                }
            };

        return
        [
            new("sqldb", "sqldb", SqlDatabase, ReadingsOf(SqlCard.Metrics), Monitor(SqlType, "/" + SqlDatabase, SqlCard.Metrics),
                Hourly(SqlType, "/" + SqlDatabase, "2023-08-01", async (resource, record, cancellation) =>
                {
                    var all = await directory.AllAsync(cancellation);
                    string server = resource.Name.Split('/')[0];
                    var paused = all.FirstOrDefault(r => string.Equals(r.Type, SqlType, StringComparison.OrdinalIgnoreCase)
                        && r.Name.StartsWith(server + "/", StringComparison.OrdinalIgnoreCase) && r.Id != resource.Id && !r.Name.EndsWith("/master", StringComparison.OrdinalIgnoreCase));
                    var facts = new Dictionary<string, string?>(SqlCard.Facts(record, model.Tables, model.Migration))
                    {
                        ["paused"] = paused?.Name.Split('/')[^1],
                        ["served_by"] = sqlSite,
                    };
                    if (paused is not null)
                    {
                        // The server's other database is read for what Azure says it is, never described from memory.
                        try
                        {
                            var (other, _) = await azure.RecordAsync(paused.Id, "2023-08-01", cancellation);
                            using (other)
                            {
                                facts["paused_status"] = AzureMetrics.Text(other.RootElement, "properties.status");
                                facts["paused_spec"] = string.Join(' ', new[] { AzureMetrics.Text(other.RootElement, "sku.tier"), AzureMetrics.Text(other.RootElement, "properties.useFreeLimit") == "true" ? "free offer" : null }.Where(p => p is not null));
                            }
                        }
                        catch (HttpRequestException)
                        {
                            // Left unread; the table says so.
                        }
                    }
                    return facts;
                }),
                SqlCard.View),
            new("cosmosdb", "cosmosdb", "cosmos-theyard-ss", ReadingsOf(CosmosCard.Metrics), Monitor(CosmosType, null, CosmosCard.Metrics),
                Hourly(CosmosType, null, "2024-05-15", async (resource, record, cancellation) =>
                {
                    using var databases = await azure.GetJsonAsync($"{resource.Id}/sqlDatabases?api-version=2024-05-15", cancellation);
                    string? database = databases.RootElement.TryGetProperty("value", out var list) && list.GetArrayLength() > 0 ? AzureMetrics.Text(list[0], "name") : null;
                    using var containers = await azure.GetJsonAsync($"{resource.Id}/sqlDatabases/{database}/containers?api-version=2024-05-15", cancellation);
                    // The database's shared throughput as Azure has it, rather than the free tier's figure written down.
                    string? throughput = null;
                    try
                    {
                        using var settings = await azure.GetJsonAsync($"{resource.Id}/sqlDatabases/{database}/throughputSettings/default?api-version=2024-05-15", cancellation);
                        throughput = AzureMetrics.Text(settings.RootElement, "properties.resource.throughput") ?? AzureMetrics.Text(settings.RootElement, "properties.resource.autoscaleSettings.maxThroughput");
                    }
                    catch (HttpRequestException)
                    {
                        // Left out of the header line rather than guessed.
                    }
                    return CosmosCard.Facts(record, containers.RootElement, throughput);
                }),
                CosmosCard.View),
            new("registry", "registry", "crtheyardsszmnetj67bn5h2", ReadingsOf(AcrCard.Metrics), Monitor(AcrType, null, AcrCard.Metrics),
                Hourly(AcrType, null, "2023-07-01", async (resource, record, cancellation) =>
                {
                    AcrHoldings? holdings = null;
                    try
                    {
                        string token = await azure.ArmTokenAsync();
                        holdings = await AcrCatalog.ReadAsync(AzureMetrics.Text(record, "properties.loginServer") ?? "", token, PlanReader.TenantOf(token) ?? "", cancellation);
                    }
                    catch (HttpRequestException)
                    {
                        // The card says the catalogue could not be read and what the identity needs.
                    }
                    // The registry's size limit as Azure states it, rather than the Basic figure written down.
                    try
                    {
                        using var usages = await azure.GetJsonAsync($"{resource.Id}/listUsages?api-version=2023-07-01", cancellation);
                        return AcrCard.Facts(record, holdings, usages.RootElement.Clone());
                    }
                    catch (HttpRequestException)
                    {
                        return AcrCard.Facts(record, holdings);
                    }
                }),
                AcrCard.View),
            new("insights", "insights", "log-theyard-ss", InsightsCard.Readings,
                async (_, to, version, cancellation) => azure.Configured ? await (await InsightsAsync(azure, directory, cancellation)).MinutesAsync(to, version, cancellation) : [],
                async (hour, since, cancellation) => azure.Configured
                    ? await (await InsightsAsync(azure, directory, cancellation)).HourAsync(hour, since, cancellation)
                    : new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []),
                InsightsCard.View, Hourly: true),
            new("mail", "mail", "acs-theyard-ss", MailCard.Readings, Monitor(MailType, null, MailCard.Metrics),
                Hourly(MailType, null, "2023-04-01", async (_, record, cancellation) =>
                {
                    string? domainId = record.TryGetProperty("properties", out var props) && props.TryGetProperty("linkedDomains", out var linked) && linked.GetArrayLength() > 0 ? linked[0].GetString() : null;
                    if (domainId is null)
                    {
                        return MailCard.Facts(record, null);
                    }
                    using var domain = await azure.GetJsonAsync($"{domainId}?api-version=2023-04-01", cancellation);
                    return MailCard.Facts(record, domain.RootElement.Clone());
                }),
                MailCard.View),
            new("edge", "edge", "theyard-edge", EdgeCard.Readings,
                async (_, to, version, cancellation) => await edge.MinutesAsync(to, version, cancellation),
                async (hour, _, cancellation) => await edge.HourAsync(hour, cancellation),
                inputs => EdgeCard.View(inputs, edge.Configured), Hourly: true),
        ];
    }

    /// <summary>The component and the workspace it sends to, which the component's own record names.</summary>
    private static async Task<InsightsSource> InsightsAsync(AzureMetrics azure, ResourceDirectory directory, CancellationToken cancellation)
    {
        var component = await directory.FindAsync(InsightsType, null, cancellation);
        var (record, _) = await azure.RecordAsync(component.Id, "2020-02-02", cancellation);
        using (record)
        {
            string workspace = AzureMetrics.Text(record.RootElement, "properties.WorkspaceResourceId")
                ?? (await directory.FindAsync(WorkspaceType, null, cancellation)).Id;
            return new InsightsSource(azure, component.Id, workspace);
        }
    }
}
