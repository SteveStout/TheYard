using TheYard.Application.Features.Docs;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// What the site serves to read: the documents, the drawings, the resume, what colleagues say, and the API's description of itself.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Nothing of its own to register yet: what its endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // The document query and its port (ADR-115's fourth rung).
        services.AddSingleton<IDocumentReadings>(provider => new DocumentReadings(provider.GetRequiredService<HostPaths>(), provider.GetRequiredService<BuildInfo>()));
        services.AddScoped<ReadDocumentHandler>();
        // The repository's marked regions, for a slice that shows the code's own words (the nightly card's processes).
        services.AddSingleton<ICodeRegions>(provider => new LiveCodeRegions(provider.GetRequiredService<HostPaths>().RepoRoot, provider.GetRequiredService<BuildInfo>().Commit));
    }

    /// <summary>Maps /api/docs/*, /about, /resume, /api/recommendations and the OpenAPI document.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapReferenceEndpoints();
        app.MapDocumentationEndpoints();
        app.MapRecommendationEndpoints();
    }
}
