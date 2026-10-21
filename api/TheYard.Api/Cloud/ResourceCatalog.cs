// The resource cards' definitions in one list: which resource each finds in the group, which metrics
// it reads a minute, what it reads an hour, and which view draws it. Off Azure every card reads
// nothing and says so, because there is no identity to ask with.
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The resource cards after the plan, in the rail's order.</summary>
public static class ResourceCatalog
{
    /// <summary>Resource Manager types and api versions the cards read.</summary>
    public const string SqlType = "Microsoft.Sql/servers/databases", CosmosType = "Microsoft.DocumentDB/databaseAccounts",
        AcrType = "Microsoft.ContainerRegistry/registries", InsightsType = "Microsoft.Insights/components",
        WorkspaceType = "Microsoft.OperationalInsights/workspaces", MailType = "Microsoft.Communication/CommunicationServices";


    /// <param name="sqlSite">The public name whose requests get the SQL database and the web app that answers it, from this process's own settings; null where it serves none.</param>
    /// <param name="names">The deployment's resource names, from configuration; the SQL card reads the database named there, and any other user database on the server is listed from its own record.</param>
    /// <param name="nightly">The nightly Function's reader, over a list of its own resource group; null leaves the card out, which only a test that builds the catalogue by hand does.</param>
    public static IReadOnlyList<ResourceCard> Cards(AzureMetrics azure, ResourceDirectory directory, NetlifyEdge edge, (int Tables, string Migration) model, AzureResources names, string? sqlSite = null, NightlySource? nightly = null)
    {
        string sqlDatabase = names.SqlDatabase;
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

        List<ResourceCard> cards =
        [
            new("sqldb", "sqldb", sqlDatabase, ReadingsOf(SqlCard.Metrics), Monitor(SqlType, "/" + sqlDatabase, SqlCard.Metrics),
                Hourly(SqlType, "/" + sqlDatabase, "2023-08-01", (_, record, _) =>
                    Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>(SqlCard.Facts(record, model.Tables, model.Migration))
                    {
                        ["served_by"] = sqlSite,
                    })),
                SqlCard.View),
            new("cosmosdb", "cosmosdb", names.Cosmos, ReadingsOf(CosmosCard.Metrics), Monitor(CosmosType, names.Cosmos, CosmosCard.Metrics),
                Hourly(CosmosType, names.Cosmos, "2024-05-15", async (resource, record, cancellation) =>
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
            new("registry", "registry", names.Registry, ReadingsOf(AcrCard.Metrics), Monitor(AcrType, names.Registry, AcrCard.Metrics),
                Hourly(AcrType, names.Registry, "2023-07-01", async (resource, record, cancellation) =>
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
            new("insights", "insights", names.Insights, InsightsCard.Readings,
                async (_, to, version, cancellation) => azure.Configured ? await (await InsightsAsync(azure, directory, names.Insights, cancellation)).MinutesAsync(to, version, cancellation) : [],
                async (hour, since, cancellation) => azure.Configured
                    ? await (await InsightsAsync(azure, directory, names.Insights, cancellation)).HourAsync(hour, since, cancellation)
                    : new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []),
                InsightsCard.View, Hourly: true, Source: "Log Analytics", SpecSource: "ARM record of the component"),
            new("mail", "mail", names.Mail, MailCard.Readings, Monitor(MailType, names.Mail, MailCard.Metrics),
                Hourly(MailType, names.Mail, "2023-04-01", async (_, record, cancellation) =>
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
            new("edge", "edge", names.Edge, EdgeCard.Readings,
                async (_, to, version, cancellation) => await edge.MinutesAsync(to, version, cancellation),
                async (hour, _, cancellation) => await edge.HourAsync(hour, cancellation),
                inputs => EdgeCard.View(inputs, edge.Configured, names.Edge, names.EdgeRetires), Hourly: true, Source: "Netlify API", SpecSource: "Netlify site record"),
        ];
        if (nightly is not null)
        {
            // Read every minute like the other Azure cards; its hour also reads the workflow's runs
            // from GitHub, and its three lines of the bill are the Function, its plan and its storage.
            NightlySource source = nightly;
            cards.Add(new("nightly", "nightly", source.Names.Function, NightlyMetrics.Readings, source.MinutesAsync, source.HourAsync,
                inputs => NightlyCard.View(inputs, source.Names, source.FunctionPath, source.Cache.Runs),
                SpecSource: "ARM record of the Function app", CostNames: source.Names.CostNames));
        }
        return cards;
    }

    /// <summary>
    /// The component and the workspace it sends to: the workspace configuration names, and the one
    /// component in the group that sends to it (its record names the workspace), or the group's
    /// one component where none says.
    /// </summary>
    private static async Task<InsightsSource> InsightsAsync(AzureMetrics azure, ResourceDirectory directory, string workspaceName, CancellationToken cancellation)
    {
        var component = await directory.FindAsync(InsightsType, null, cancellation);
        var (record, _) = await azure.RecordAsync(component.Id, "2020-02-02", cancellation);
        using (record)
        {
            string workspace = AzureMetrics.Text(record.RootElement, "properties.WorkspaceResourceId")
                ?? (await directory.FindAsync(WorkspaceType, workspaceName, cancellation)).Id;
            return new InsightsSource(azure, component.Id, workspace);
        }
    }
}
