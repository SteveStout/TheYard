using TheYard.Application.Features.Review;
using TheYard.Infrastructure.Cosmos.Features.Review;

namespace TheYard.Api.Features.Review;

/// <summary>
/// The review route's reach (ADR: Ten minutes, seven stops, the addendum on the reach rows): where it is
/// kept, the collector that writes it off the request path, the command and the query, and the two routes.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the keeper, its reading side, the collector and the two handlers.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration; the slice reads nothing of it.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region review-wiring
        // The document store keeps the reach beside the site's activity wherever it is configured, as the
        // link opens are kept; a container without one keeps it in memory, which is what the tests run on.
        services.AddSingleton<IReviewStore>(provider => provider.GetRequiredService<DocumentStore>().Store is { } cosmos
            ? new CosmosReviewStore(cosmos)
            : new MemoryReviewStore());
        services.AddSingleton<IReviewReader>(provider => provider.GetRequiredService<IReviewStore>());
        services.AddSingleton(provider => new ReviewReachCollector(provider.GetRequiredService<IReviewStore>(), provider.GetRequiredService<ILogger<ReviewReachCollector>>()));
        services.AddHostedService(provider => provider.GetRequiredService<ReviewReachCollector>());
        services.AddSingleton<IReviewReachOffers>(provider => provider.GetRequiredService<ReviewReachCollector>());
        services.AddScoped(provider => new RecordReviewReachHandler(provider.GetRequiredService<IReviewReachOffers>(), TimeProvider.System));
        services.AddScoped(provider => new ReadReviewFunnelHandler(provider.GetRequiredService<IReviewReader>(), TimeProvider.System));
        // #endregion review-wiring
    }

    /// <summary>Maps the slice's two routes.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapReviewEndpoints();
}
