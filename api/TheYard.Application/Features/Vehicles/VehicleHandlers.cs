// The catalogue as the page reads it: a page of vehicles at their standing prices, the values
// behind the filters, and one vehicle, each answered by the request's auction.
using TheYard.Domain;

namespace TheYard.Application.Features.Vehicles;

// #region vehicle-handlers
/// <summary>
/// A page of the catalogue (ADR: The onion cut into vertical slices, rule 7): filtered, sorted and
/// paged in the store, each vehicle at its standing price with its sold flag, so a listing never
/// composes the bids, the room or the sale itself.
/// </summary>
/// <param name="auction">The request's auction.</param>
public sealed class SearchVehiclesHandler(Auction auction)
{
    /// <summary>One page.</summary>
    /// <param name="request">The filter, the clock it was read on, the sort and the page.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<StandingPage> HandleAsync(VehicleSearch request, CancellationToken cancellation) =>
        auction.SearchAsync(request.Filter, request.Clock, request.Sort, request.Limit, request.Offset);
}

/// <summary>The distinct values behind the filters, across the whole catalogue.</summary>
/// <param name="auction">The request's auction.</param>
public sealed class FacetsHandler(Auction auction)
{
    /// <summary>The makes, body styles, title statuses and provinces.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<InventoryFacets> HandleAsync(CancellationToken cancellation) => auction.FacetsAsync();
}

/// <summary>One vehicle at its standing price, on the server's clock.</summary>
/// <param name="auction">The request's auction.</param>
/// <param name="clock">The server's clock.</param>
public sealed class FindVehicleHandler(Auction auction, Func<AuctionClock> clock)
{
    /// <summary>The vehicle and the clock its facts are derived on, or no vehicle.</summary>
    /// <param name="id">The vehicle's id.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<FoundVehicle> HandleAsync(string id, CancellationToken cancellation) =>
        new(await auction.FindAsync(id), clock());
}
// #endregion vehicle-handlers

/// <summary>A search of the catalogue.</summary>
/// <param name="Filter">What to keep.</param>
/// <param name="Clock">The server's clock the filter's statuses were read on.</param>
/// <param name="Sort">The order.</param>
/// <param name="Limit">How many.</param>
/// <param name="Offset">From where.</param>
public sealed record VehicleSearch(VehicleFilter Filter, AuctionClock Clock, VehicleSort Sort, int Limit, int Offset);

/// <summary>One vehicle, or none, and the clock its facts are derived on.</summary>
/// <param name="Vehicle">The vehicle at its standing price, or null when no vehicle has the id.</param>
/// <param name="Clock">The server's clock.</param>
public sealed record FoundVehicle(StandingVehicle? Vehicle, AuctionClock Clock);
