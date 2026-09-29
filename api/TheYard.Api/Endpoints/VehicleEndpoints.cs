using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;
using TheYard.Data;

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
        // The store this request chose, and everything that stands on it
        // (ADR: One container, both stores).
        var (inventory, bids, market) = current;
        if (!query.TryBuildFilter(out var filter, out var clock, out var sort, out var error))
        {
            // One failure shape for the whole API (ADR-023): the message a person
            // can act on goes in `detail`, never in a key only this endpoint uses.
            return TypedResults.Problem(detail: error, statusCode: 400, title: "The query could not be read");
        }
        // #region overlays
        // The buyer's bids first, the room's second, and the room only wins where
        // it is actually higher (ADR-027). In the other order the buyer would
        // always look like the high bidder, which is the bug this feature exists
        // to make impossible. Both are skipped entirely when nobody has bid, so
        // the common cold request pays for neither.
        // IsEmpty, not Snapshot().Count: a snapshot is a full dictionary copy, and
        // copying both of them on every inventory request to ask whether they are
        // empty is work that grows with the number of bids ever placed.
        Func<Vehicle, Vehicle>? overlay = (bids.IsEmpty, market.IsEmpty) switch
        {
            (true, true) => null,
            (false, true) => bids.Apply,
            (true, false) => market.Apply,
            _ => vehicle => market.Apply(bids.Apply(vehicle)),
        };
        // #endregion overlays
        var result = inventory.Search(filter, clock, sort, query.EffectiveLimit, query.EffectiveOffset, overlay);
        return TypedResults.Ok(
            new VehiclePage(
                result.Total,
                [.. result.Vehicles.Select(v => VehicleWire.ToWire(v, clock, bids.IsSold(v.Id)))]));
    }
    #endregion inventory-endpoint

    private static Ok<InventoryFacets> Facets(CurrentBackend current) =>
        TypedResults.Ok(current.Inventory.Facets());

    private static Results<Ok<VehicleView>, ProblemHttpResult> One(CurrentBackend current, string id)
    {
        var (inventory, bids, market) = current;
        var clock = Clocks.Now();
        return inventory.GetById(id) is { } vehicle
            ? TypedResults.Ok(
                VehicleWire.ToWire(market.Apply(bids.Apply(vehicle)), clock, bids.IsSold(vehicle.Id)))
            : TypedResults.Problem(detail: "No vehicle has that id.", statusCode: 404, title: "No such vehicle");
    }
}
