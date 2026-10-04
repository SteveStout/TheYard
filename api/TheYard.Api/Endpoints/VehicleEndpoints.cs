using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>
/// The catalogue: search with filters, sort and paging, the values behind the
/// filters, and one vehicle. Anonymous reads on the store the request chose;
/// every reply is a named record carrying the server's auction facts.
/// </summary>
public static class VehicleEndpoints
{
    /// <summary>Maps the catalogue routes under /api/vehicles and /api/facets.</summary>
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/vehicles", List)
            .WithName("ListVehicles")
            .WithTags("Vehicles")
            .WithSummary("Search the catalogue")
            .WithDescription("Every filter, the sort and the page are optional query parameters, applied on the server. "
                + "The default page is the top hundred by auction time, live and ending soonest first. Each vehicle "
                + "carries the server-derived auction facts, so a client formats and counts down and never "
                + "re-implements the schedule.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Dropdown values, computed from the full dataset (the page only ever holds a slice).
        app.MapGet("/api/facets", Facets)
            .WithName("GetFacets")
            .WithTags("Vehicles")
            .WithSummary("The distinct values behind the filters")
            .WithDescription("Makes, body styles, title statuses and provinces across the whole catalogue, for the dropdowns.");

        app.MapGet("/api/vehicles/{id}", One)
            .WithName("GetVehicle")
            .WithTags("Vehicles")
            .WithSummary("One vehicle, with its auction facts")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    #region inventory-endpoint
    // All filters, sorting, and paging are optional GET parameters, applied
    // server-side. The default page is the top 100 by auction time (live and
    // ending soonest first). Responses are an envelope: { total, vehicles },
    // with each vehicle carrying the server-derived auction facts.
    // e.g. /api/vehicles?make=Ford&status=live&sort=price-asc&limit=100&offset=100
    private static Results<Ok<VehiclePage>, ProblemHttpResult> List(
        CurrentBackend current,
        [AsParameters] VehicleQueryParams query)
    {
        if (!query.TryBuildFilter(out var filter, out var clock, out var sort, out var error))
        {
            // One failure shape for the whole API: the message a person can act on goes in
            // `detail`, never in a key only this endpoint uses.
            return TypedResults.Problem(detail: error, statusCode: 400, title: "The query could not be read");
        }
        // The auction composes the bids over the catalogue in the order the rules need, so a
        // listing never decides that order itself.
        var auction = current.Auction;
        var result = auction.Search(filter, clock, sort, query.EffectiveLimit, query.EffectiveOffset);
        return TypedResults.Ok(
            new VehiclePage(
                result.Total,
                [.. result.Vehicles.Select(v => VehicleWire.ToWire(v, clock, auction.IsSold(v.Id)))]));
    }
    #endregion inventory-endpoint

    private static Ok<InventoryFacets> Facets(CurrentBackend current) =>
        TypedResults.Ok(current.Auction.Facets());

    private static Results<Ok<VehicleView>, ProblemHttpResult> One(CurrentBackend current, string id)
    {
        var auction = current.Auction;
        var clock = Clocks.Now();
        return auction.Find(id) is { } vehicle
            ? TypedResults.Ok(VehicleWire.ToWire(auction.AsItStands(vehicle), clock, auction.IsSold(vehicle.Id)))
            : TypedResults.Problem(detail: "No vehicle has that id.", statusCode: 404, title: "No such vehicle");
    }
}
