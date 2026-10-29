using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>
/// The eleven resource cards under Cloud infrastructure (ADR: One frame for every resource): one
/// reader of Azure Monitor, one of Netlify and one of GitHub, the resource groups' lists each card
/// finds its resource in (the nightly Function's group is its own, ADR: The Nightly Function on the
/// Admin tab), a keep per card, the nightly run log and its cache, the one recorder, the one cached reader the endpoint answers from,
/// the endpoint, and the one handler a card is fed through. Each card's own file says what it reads and how it draws.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the cards, their keeps, the recorder and the reader.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration: the identity, the edge's token and the names.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region resource-wiring
        string clientId = AzureIds.ClientId(configuration);
        var names = AzureResources.From(configuration);
        string group = names.GroupPath(AzureIds.SubscriptionId(configuration));
        string? token = configuration["Netlify:Token"];
        var publicSite = configuration["Netlify:PublicSite"] is { } site ? new Uri(site) : names.PublicSite;
        // Which public name gets the SQL database, from this process's own settings.
        string? sqlName = SiteSettings.From(configuration).HostFor(StoreKeys.Relational);
        // The nightly Function lives in a resource group of its own, listed by a directory of its own.
        var nightlyNames = NightlyResources.From(configuration);
        string subscription = AzureIds.SubscriptionId(configuration);
        string campaignDatabase = configuration["Azure:Resources:CampaignDatabase"] ?? throw new InvalidOperationException("Azure:Resources:CampaignDatabase is not configured.");
        services.AddSingleton<NightlyRunCache>();
        services.AddSingleton<INightlyRunLog>(provider => provider.GetRequiredService<DocumentStore>().Store is { } store
            ? (INightlyRunLog)new CosmosNightlyRunLog(store)
            : new MemoryNightlyRunLog("kept in this process's memory, so the run log ends with it; the document store is not configured here"));
        services.AddSingleton<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            var azure = new AzureMetrics(clientId, configured: identity.OnAppService, identity: identity);
            var directory = new ResourceDirectory(azure, group, TimeProvider.System);
            var nightlyDirectory = new ResourceDirectory(azure, nightlyNames.GroupPath(subscription), TimeProvider.System);
            var runLog = provider.GetRequiredService<INightlyRunLog>();
            var runReader = new NightlyRunReader(new GitHubRuns(nightlyNames.Repo, nightlyNames.Workflow, configuration["Nightly:GitHubToken"]), runLog, provider.GetRequiredService<NightlyRunCache>(), TimeProvider.System);
            var nightly = new NightlySource(azure, nightlyDirectory, nightlyNames, runReader, runLog, nightlyNames.FunctionPath(subscription));
            // One hashed bundle file from the built page, so the hourly sample scores a file the edge keeps.
            string builtPage = Path.Combine(Path.GetDirectoryName(provider.GetRequiredService<HostPaths>().AssetsRoot)!, "index.html");
            var edge = new NetlifyEdge(token, names.Edge, publicSite, NetlifyEdge.BundleFileOf(builtPage));
            string? sqlSite = sqlName is null ? null : identity.SiteName is { } siteName ? $"{sqlName}, on {siteName}" : sqlName;
            var cosmos = provider.GetRequiredService<DocumentStore>().Store;
            IResourceHistory KeepOf(ResourceCard card) => cosmos is not null
                ? new CosmosResourceHistory(cosmos, card.Container, card.KindPrefix)
                : new MemoryResourceHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
            // The registry card names which web app runs each image from the plan card's own hour, the
            // one read of every app's linuxFxVersion, rather than reading the apps a second time.
            var planHistory = provider.GetRequiredService<IPlanHistory>();
            async Task<PlanHour?> NewestPlanHour(CancellationToken cancellation) =>
                (await planHistory.HoursAsync(TimeProvider.System.GetUtcNow().AddHours(-2), cancellation)) is { Count: > 0 } kept ? kept[^1] : null;
            var claude = new ClaudeSource(provider.GetRequiredService<IUsageLog>(), TimeProvider.System);
            // The job hunt's storage account is in the nightly Function's group, listed by that group's directory.
            var storage = new StorageSource(azure, nightlyDirectory, nightlyNames, provider.GetRequiredService<ICostHistory>(), new BlobListing(clientId, identity.OnAppService, identity));
            // The email campaign's card reads its database over the site's own server connection, the catalog changed,
            // as an identity that may read the card schema and nothing else (ADR-118, Decision 6).
            var campaign = new CampaignSource(
                new SqlCampaignCounts(SqlCampaignCounts.For(configuration.GetConnectionString("YardSql"), campaignDatabase)),
                cancellation => NightlyProcesses.HostAsync(azure, nightlyNames.FunctionPath(subscription), cancellation),
                async cancellation => azure.Configured && ResourceDirectory.Find(await directory.AllAsync(cancellation), ResourceCatalog.SqlType, "/" + campaignDatabase) is { } found
                    ? (found.Id, (string?)null)
                    : null,
                campaignDatabase);
            return [.. ResourceCatalog.Cards(azure, directory, edge, YardModelFacts.Read(), names, sqlSite, nightly, NewestPlanHour, claude, storage, BlobLines.From(configuration), campaign).Select(card => (card, KeepOf(card)))];
        });

        // One reading of a shared resource is the whole truth, so one process records it, as with
        // the plan: the one whose default store is SQL, which the one web app is, on App Service.
        bool records = string.Equals(StoreChoice.DefaultKey(configuration), StoreKeys.Relational, StringComparison.Ordinal)
            && HostIdentity.From(configuration).OnAppService;
        if (records)
        {
            services.AddSingleton(provider => new CloudRecorder(
                provider.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>(),
                provider.GetRequiredService<BuildInfo>(),
                TimeProvider.System,
                provider.GetRequiredService<ILogger<CloudRecorder>>()));
            services.AddHostedService(provider => provider.GetRequiredService<CloudRecorder>());
        }
        // The Function's runs, from the workspace both Application Insights components send to, by its app name
        // as its cloud role; the six sections and the run histories read them (ADR-109, the six processes).
        string workspace = $"{names.GroupPath(subscription)}/providers/Microsoft.OperationalInsights/workspaces/{names.Insights}";
        services.AddSingleton<IProcessRunsReader>(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            return new GuardedProcessRuns(new InsightsProcessRuns(new AzureMetrics(clientId, configured: identity.OnAppService, identity: identity), workspace, nightlyNames.Function), TimeProvider.System);
        });
        services.AddSingleton(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            var azure = new AzureMetrics(clientId, configured: identity.OnAppService, identity: identity);
            var build = provider.GetRequiredService<BuildInfo>();
            string functionPath = nightlyNames.FunctionPath(subscription);
            return new NightlyProcesses(
                provider.GetRequiredService<IProcessRunsReader>(),
                async cancellation => await NightlyProcesses.HostAsync(azure, functionPath, cancellation),
                new ProcessDescriptions(provider.GetRequiredService<ICodeRegions>()),
                build.Version,
                TimeProvider.System);
        });
        services.AddSingleton(provider => new CloudReader(
            provider.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>(),
            records ? provider.GetRequiredService<CloudRecorder>() : null,
            (name, cancellation) => PlanCost.ForResourceAsync(provider.GetRequiredService<ICostHistory>(), name, TimeProvider.System, cancellation),
            TimeProvider.System,
            sqlName ?? "the SQL site",
            provider.GetRequiredService<NightlyProcesses>()));
        services.AddSingleton<ICloudCards>(provider => provider.GetRequiredService<CloudReader>());
        services.AddScoped<ReadCloudCardHandler>();
        services.AddScoped<ReadProcessRunsHandler>();
        // The one write in this slice: the nightly workflow posting the run it was, which the card's
        // reader then prefers over a read of GitHub (ADR: The Nightly Function on the Admin tab). It
        // writes the same log the hourly read upserts into, keyed on GitHub's id, so the pushed copy
        // and the read copy are one document.
        services.AddScoped(provider => new KeepNightlyRunHandler(provider.GetRequiredService<INightlyRunLog>(), TimeProvider.System));
        // What broke: the incidents the docs keep and the failures the same workspace kept (ADR-120).
        services.AddSingleton<IIncidentsReader>(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            return new InsightsIncidents(new AzureMetrics(clientId, configured: identity.OnAppService, identity: identity), workspace, provider.GetRequiredService<HostPaths>().RepoRoot, TimeProvider.System);
        });
        services.AddScoped<ReadIncidentsHandler>();
        // #endregion resource-wiring
    }

    /// <summary>Maps the cards' reads and the nightly run's push.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapCloudEndpoints();
}
