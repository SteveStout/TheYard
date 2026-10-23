using TheYard.Application.Features.Vehicles;

namespace TheYard.Api.Features.Vehicles;

/// <summary>
/// The catalogue (ADR: The store does the work): a page of vehicles, one vehicle, and the filter
/// values. Each route asks one handler in the Application ring's Features/Vehicles, handed the
/// request's auction and the server's clock (ADR: The onion cut into vertical slices, rule 7).
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the slice's handlers, one per request, each over the request store's reading sides.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped(provider => new SearchVehiclesHandler(provider.GetRequiredService<CurrentBackend>().Standings));
        services.AddScoped(provider => new ReadFacetsHandler(provider.GetRequiredService<CurrentBackend>().Backend.Inventory));
        services.AddScoped(provider => new FindVehicleHandler(provider.GetRequiredService<CurrentBackend>().Standings, Clocks.Now));
    }

    /// <summary>Maps /api/vehicles, /api/vehicles/{id} and /api/facets.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapVehicleEndpoints();
}
