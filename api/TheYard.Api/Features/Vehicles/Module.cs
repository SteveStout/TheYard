namespace TheYard.Api.Features.Vehicles;

/// <summary>
/// The catalogue (ADR: The store does the work): a page of vehicles, one vehicle, and the filter values.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Nothing of its own to register yet: what its endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>Maps /api/vehicles, /api/vehicles/{id} and /api/facets.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapVehicleEndpoints();
    }
}
