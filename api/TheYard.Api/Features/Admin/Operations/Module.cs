namespace TheYard.Api.Features.Admin.Operations;

/// <summary>
/// The Admin tab's operator reads (ADR: What the database is actually doing): the rings, the kept log, the activity, the machines, the proof, the page sweep, the costs, the telemetry and the stores.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Nothing of its own to register yet: what its endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>Maps /api/admin/* that is not the plan or a cloud card, /api/stores and /api/tests/summary.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapAdminEndpoints();
    }
}
