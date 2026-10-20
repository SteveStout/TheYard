using TheYard.Application;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>
/// The six resource cards under Cloud infrastructure (ADR: One frame for every resource): one
/// reader of Azure Monitor and one of Netlify, the resource group's list each card finds its
/// resource in, a keep per card, the one recorder, the one cached reader the endpoint answers from,
/// and the endpoint. Each card's own file says what it reads and how it draws.
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
        string? sqlName = SiteSettings.From(configuration).HostFor("sql");
        services.AddSingleton<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            var azure = new AzureMetrics(clientId, configured: identity.OnAppService, identity: identity);
            var directory = new ResourceDirectory(azure, group, TimeProvider.System);
            var edge = new NetlifyEdge(token, names.Edge, publicSite);
            string? sqlSite = sqlName is null ? null : identity.SiteName is { } siteName ? $"{sqlName}, on {siteName}" : sqlName;
            var cosmos = provider.GetRequiredService<DocumentStore>().Store;
            IResourceHistory KeepOf(ResourceCard card) => cosmos is not null
                ? new CosmosResourceHistory(cosmos, card.Container)
                : new MemoryResourceHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
            return [.. ResourceCatalog.Cards(azure, directory, edge, YardModelFacts.Read(), names, sqlSite).Select(card => (card, KeepOf(card)))];
        });

        // One reading of a shared resource is the whole truth, so one process records it, as with
        // the plan: the one whose default store is SQL, which the one web app is, on App Service.
        bool records = string.Equals(StoreChoice.DefaultKey(configuration), "sql", StringComparison.Ordinal)
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
        services.AddSingleton(provider => new CloudReader(
            provider.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>(),
            records ? provider.GetRequiredService<CloudRecorder>() : null,
            (name, cancellation) => PlanCost.ForResourceAsync(provider.GetRequiredService<ICostHistory>(), name, TimeProvider.System, cancellation),
            TimeProvider.System,
            sqlName ?? "the SQL site"));
        services.AddSingleton<ICloudCards>(provider => provider.GetRequiredService<CloudReader>());
        services.AddScoped<ReadCloudCardHandler>();
        // #endregion resource-wiring
    }

    /// <summary>Maps GET /api/admin/cloud/{key}.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapCloudEndpoints();
}
