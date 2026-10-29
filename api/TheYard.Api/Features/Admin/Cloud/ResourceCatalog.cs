// The resource cards' definitions in one list: which resource each finds in the group, which metrics
// it reads a minute, what it reads an hour, and which view draws it. Off Azure every card reads
// nothing and says so, because there is no identity to ask with.
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

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
    /// <param name="planHour">The plan card's newest kept hour, whose apps name the image each runs; the registry card reads it rather than asking Azure a second time. Null reads none.</param>
    /// <param name="claude">The usage documents' reader for the Claude usage card; null leaves the card out, which only a test that builds the catalogue by hand does.</param>
    /// <param name="storage">The job hunt's storage account's reader for the share and blob cards; null leaves both out, which only a test that builds the catalogue by hand does.</param>
    /// <param name="blobLines">The lines the blob card is read against, from configuration.</param>
    /// <param name="campaign">The email campaign's reads for its card; null leaves the card out, which only a test that builds the catalogue by hand does.</param>
    public static IReadOnlyList<ResourceCard> Cards(AzureMetrics azure, ResourceDirectory directory, NetlifyEdge edge, (int Tables, string Migration) model, AzureResources names, string? sqlSite = null, NightlySource? nightly = null, Func<CancellationToken, Task<PlanHour?>>? planHour = null, ClaudeSource? claude = null, StorageSource? storage = null, BlobLines? blobLines = null, CampaignSource? campaign = null)
    {
        string sqlDatabase = names.SqlDatabase;
        static IReadOnlyList<string> ReadingsOf(IReadOnlyList<AzureMetric> metrics) => [.. metrics.Select(m => m.Reading).Distinct()];

        // A resource the group does not hold reads no minutes, and its hour says so in a sentence
        // the card prints as its note, rather than an exception only the log sees.
        Func<DateTimeOffset, DateTimeOffset, string, CancellationToken, Task<IReadOnlyList<ResourceMinute>>> Monitor(string type, string? name, IReadOnlyList<AzureMetric> metrics) =>
            async (from, to, version, cancellation) => azure.Configured && ResourceDirectory.Find(await directory.AllAsync(cancellation), type, name) is { } resource
                ? await azure.MinutesAsync(resource.Id, metrics, from, to, version, cancellation)
                : [];

        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<ResourceHour>> Hourly(string type, string? name, string apiVersion, Func<AzureResource, JsonElement, CancellationToken, Task<IReadOnlyDictionary<string, string?>>> facts) =>
            async (hour, since, cancellation) =>
            {
                if (!azure.Configured)
                {
                    return new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []);
                }
                if (ResourceDirectory.Find(await directory.AllAsync(cancellation), type, name) is not { } resource)
                {
                    return new ResourceHour(hour, "", null, new Dictionary<string, string?> { [ResourceView.MissingFact] = ResourceDirectory.Missing(name) }, []);
                }
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
                        string token = await azure.ArmTokenAsync(cancellation);
                        holdings = await AcrCatalog.ReadAsync(AzureMetrics.Text(record, "properties.loginServer") ?? "", token, PlanReader.TenantOf(token) ?? "", cancellation);
                    }
                    catch (HttpRequestException)
                    {
                        // The card says the catalogue could not be read and what the identity needs.
                    }
                    // GitHub's registry beside it, read as an anonymous pull reads it (ADR-009, the addendum on GitHub's registry).
                    string ghcr = nightly is null ? "not read" : GhcrPackages.Words(await GhcrPackages.ReadAsync(nightly.Names.Repo.Split('/')[0].ToLowerInvariant(), GhcrPackages.Images, cancellation));
                    // The registry's size limit as Azure states it, rather than the Basic figure written down.
                    try
                    {
                        using var usages = await azure.GetJsonAsync($"{resource.Id}/listUsages?api-version=2023-07-01", cancellation);
                        return AcrCard.Facts(record, holdings, usages.RootElement.Clone(), planHour is null ? null : await planHour(cancellation), ghcr);
                    }
                    catch (HttpRequestException)
                    {
                        return AcrCard.Facts(record, holdings, plan: planHour is null ? null : await planHour(cancellation), ghcr: ghcr);
                    }
                }),
                AcrCard.View),
            new("insights", "insights", names.Insights, InsightsCard.Readings,
                async (_, to, version, cancellation) => azure.Configured && await InsightsAsync(azure, directory, names.Insights, cancellation) is { } source ? await source.MinutesAsync(to, version, cancellation) : [],
                async (hour, since, cancellation) => !azure.Configured
                    ? new ResourceHour(hour, "", null, new Dictionary<string, string?>(), [])
                    : await InsightsAsync(azure, directory, names.Insights, cancellation) is { } source
                        ? await source.HourAsync(hour, since, cancellation)
                        : new ResourceHour(hour, "", null, new Dictionary<string, string?> { [ResourceView.MissingFact] = ResourceDirectory.Missing(names.Insights) }, []),
                InsightsCard.View, Hourly: true, SpecSource: "ARM record of the component"),
            // #region storage-cards
            // The job hunt's storage account, after Application Insights in the rail: its file share and its
            // blob service, each a card of its own. Their minutes are kept in the plan container under kinds
            // of their own, as the nightly card's are, because the shared database is at twenty-five containers.
            .. storage is null ? (IEnumerable<ResourceCard>)[] :
            [
                new("share", "plan", storage.Names.Storage, ReadingsOf(ShareCard.Metrics),
                    (from, to, version, cancellation) => storage.MinutesAsync("fileServices", ShareCard.Metrics, from, to, version, cancellation),
                    storage.ShareHourAsync, ShareCard.View, SpecSource: "Azure Resource Manager", KindPrefix: "share-"),
                new("blob", "plan", storage.Names.Storage, ReadingsOf(BlobCard.Metrics),
                    (from, to, version, cancellation) => storage.MinutesAsync("blobServices", BlobCard.Metrics, from, to, version, cancellation),
                    storage.BlobHourAsync, inputs => BlobCard.View(inputs, blobLines ?? new BlobLines(1_000_000_000, 1_800_000_000, 24_000)), SpecSource: "Azure Resource Manager", KindPrefix: "blob-"),
            ],
            // #endregion storage-cards
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
                inputs => EdgeCard.View(inputs, edge.Configured, names.Edge, names.EdgeRetires), Hourly: true, SpecSource: "Netlify site record"),
        ];
        if (nightly is not null)
        {
            // Read every minute like the other Azure cards; its hour also reads the workflow's runs
            // from GitHub, and its three lines of the bill are the Function, its plan and its storage.
            // Its minutes and hours are kept in the plan container under kinds of its own, because the
            // shared database is at its twenty-five containers.
            NightlySource source = nightly;
            cards.Add(new("nightly", "plan", source.Names.Function, NightlyMetrics.Readings, source.MinutesAsync, source.HourAsync,
                inputs => NightlyCard.View(inputs, source.Names, source.FunctionPath, source.Cache.Runs),
                SpecSource: "ARM record of the Function app", CostNames: source.Names.CostNames, KindPrefix: "nightly-"));
        }
        if (claude is not null)
        {
            // After the nightly card, whose run log its slot row joins on. Its readings are the usage
            // documents the lanes post, read back each minute; its own minute keeps two counts in the
            // plan container under kinds of its own, like the nightly card's.
            ClaudeSource usage = claude;
            cards.Add(new("claude", "plan", "Claude", [ClaudeSource.RunsToday, ClaudeSource.TokensOutToday], usage.MinutesAsync, usage.HourAsync,
                inputs => ClaudeCard.View(inputs, usage, nightly?.Cache.Runs ?? [], nightly?.Names.Repo),
                SpecSource: ClaudeCard.Documents, KindPrefix: "claude-"));
        }
        if (campaign is not null)
        {
            // After the Claude usage card: the email campaign (ADR-118), its minutes today's counts from the campaign
            // database's card schema, kept in the plan container under kinds of its own like the nightly card's.
            CampaignSource source = campaign;
            cards.Add(new("campaign", "plan", source.Database, CampaignSource.Readings, source.MinutesAsync, source.HourAsync, CampaignCard.View,
                SpecSource: "functions/JobHunt.Database and the Function's settings", KindPrefix: "campaign-"));
        }
        return cards;
    }

    /// <summary>
    /// The component and the workspace it sends to, both by the workspace configuration names: the
    /// component whose record names that workspace, or the group's one component when there is one
    /// and its record names none. Null when the group holds no such pair, which the card says.
    /// </summary>
    public static async Task<InsightsSource?> InsightsAsync(AzureMetrics azure, ResourceDirectory directory, string workspaceName, CancellationToken cancellation)
    {
        var all = await directory.AllAsync(cancellation);
        var components = all.Where(r => string.Equals(r.Type, InsightsType, StringComparison.OrdinalIgnoreCase)).ToList();
        if (ResourceDirectory.Find(all, WorkspaceType, workspaceName) is not { } named)
        {
            return null;
        }
        foreach (var component in components)
        {
            var (record, _) = await azure.RecordAsync(component.Id, "2020-02-02", cancellation);
            using (record)
            {
                string? workspace = AzureMetrics.Text(record.RootElement, "properties.WorkspaceResourceId");
                if (string.Equals(workspace, named.Id, StringComparison.OrdinalIgnoreCase) || (workspace is null && components.Count == 1))
                {
                    return new InsightsSource(azure, component.Id, named.Id);
                }
            }
        }
        return null;
    }
}
