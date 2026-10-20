namespace TheYard.Api.Features.Health;

/// <summary>
/// Whether the site is well: the health report, the probes, the version, the first screen's reading and the error record.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Nothing of its own to register yet: what its endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>Maps /api/health, /healthz, /readyz, /api/version, /api/pulse and /api/errors.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapHealthEndpoints();
        app.MapPulseEndpoints();
        app.MapErrorEndpoints();
    }
}
