// The Visitors slice (ADR-116: People who have visited, counted once): the count of people who have visited,
// all time, and the kept visits beside it, for the Admin tab's readouts. The store is the shared one ActivityRegistration builds, because the
// collector that writes it is shared too; this slice registers the one query and maps its one route.
using TheYard.Application.Features.Visitors;

namespace TheYard.Api.Features.Visitors;

/// <summary>Registers and maps the Visitors slice.</summary>
public sealed class Module : IModule
{
    /// <summary>Registers the count's query handler, over the kept visitors' reading side and the server's clock.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped(provider => new ReadPeopleCountHandler(provider.GetRequiredService<IVisitorReader>(), TimeProvider.System, provider.GetRequiredService<IVisitHistory>()));

    /// <summary>Maps /api/admin/visitors/people.</summary>
    /// <param name="app">The host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapVisitorEndpoints();
}
