using TheYard.Application.Features.Links;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Features.Links;

/// <summary>
/// Tagged links (ADR: Tagged links, who opened what): where opens are kept, the collector that
/// writes them off the request path, the configured pass-through addresses, the three handlers
/// and the routes. The request recorder offers each open; this slice keeps and shows them.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the keeper, the collector, the targets and the handlers.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration: the pass-through addresses under Links:Go.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region link-wiring
        // The document store keeps opens beside the site's activity wherever it is configured;
        // a container without one keeps them in memory, which is what the tests run on.
        services.AddSingleton<ILinkOpens>(provider => provider.GetRequiredService<DocumentStore>().Store is { } cosmos
            ? new CosmosLinkOpens(cosmos)
            : new MemoryLinkOpens());
        services.AddSingleton(provider => new LinkOpenCollector(provider.GetRequiredService<ILinkOpens>(), provider.GetRequiredService<ILogger<LinkOpenCollector>>()));
        services.AddHostedService(provider => provider.GetRequiredService<LinkOpenCollector>());
        // The shared request recorder offers through the Application port, so it names no slice.
        services.AddSingleton<ILinkOpenOffers>(provider => provider.GetRequiredService<LinkOpenCollector>());
        services.AddSingleton<ILinkTargets>(new ConfiguredLinkTargets(configuration));
        services.AddScoped<GoHandler>();
        services.AddScoped<ReportPageOpenHandler>();
        services.AddScoped(provider => new ReadLinkOpensHandler(provider.GetRequiredService<ILinkOpens>(), provider.GetRequiredService<ILinkTargets>(), TimeProvider.System));
        // #endregion link-wiring
    }

    /// <summary>Maps the slice's three routes.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapLinkEndpoints();
}
