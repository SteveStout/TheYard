// The catalogue as the page reads it: a page of vehicles at their standing prices, the values
// behind the filters, and one vehicle, each answered from the request store's reading sides.
using TheYard.Application.Shared;
using TheYard.Domain;

namespace TheYard.Application.Features.Vehicles;

// #region vehicle-handlers
/// <summary>
/// A page of the catalogue (ADR: The onion cut into vertical slices, rule 7): filtered, sorted and
/// paged in the store, each vehicle at its standing price with its sold flag, so a listing never
/// composes the bids, the room or the sale itself.
/// </summary>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
public sealed class SearchVehiclesHandler(StandingReader standings) : IQueryHandler<SearchVehiclesQuery, StandingPage>
{
    /// <summary>One page.</summary>
    /// <param name="request">The filter, the clock it was read on, the sort and the page.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<StandingPage> HandleAsync(SearchVehiclesQuery request, CancellationToken cancellation) =>
        standings.SearchAsync(request.Filter, request.Clock, request.Sort, request.Limit, request.Offset);
}

/// <summary>The distinct values behind the filters, across the whole catalogue.</summary>
/// <param name="inventory">The catalogue, reading side.</param>
public sealed class ReadFacetsHandler(IStockReader inventory) : IQueryHandler<ReadFacetsQuery, InventoryFacets>
{
    /// <summary>The makes, body styles, title statuses and provinces.</summary>
    /// <param name="query">The facets, which take nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<InventoryFacets> HandleAsync(ReadFacetsQuery query, CancellationToken cancellation) => inventory.FacetsAsync();
}

/// <summary>The values behind the filters; there is nothing to ask beyond the asking.</summary>
public sealed record ReadFacetsQuery;

/// <summary>One vehicle at its standing price, on the server's clock.</summary>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
/// <param name="clock">The server's clock.</param>
public sealed class FindVehicleHandler(StandingReader standings, Func<AuctionClock> clock) : IQueryHandler<FindVehicleQuery, FoundVehicle>
{
    /// <summary>The vehicle and the clock its facts are derived on, or no vehicle.</summary>
    /// <param name="query">Which vehicle.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<FoundVehicle> HandleAsync(FindVehicleQuery query, CancellationToken cancellation) =>
        new(await standings.FindAsync(query.Id), clock());
}

/// <summary>One vehicle by its id.</summary>
/// <param name="Id">The vehicle's id.</param>
public sealed record FindVehicleQuery(string Id);
// #endregion vehicle-handlers

/// <summary>A search of the catalogue.</summary>
/// <param name="Filter">What to keep.</param>
/// <param name="Clock">The server's clock the filter's statuses were read on.</param>
/// <param name="Sort">The order.</param>
/// <param name="Limit">How many.</param>
/// <param name="Offset">From where.</param>
public sealed record SearchVehiclesQuery(VehicleFilter Filter, AuctionClock Clock, VehicleSort Sort, int Limit, int Offset);

/// <summary>One vehicle, or none, and the clock its facts are derived on.</summary>
/// <param name="Vehicle">The vehicle at its standing price, or null when no vehicle has the id.</param>
/// <param name="Clock">The server's clock.</param>
public sealed record FoundVehicle(StandingVehicle? Vehicle, AuctionClock Clock);
