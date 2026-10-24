// The count of people who have visited as the Admin tab reads it (ADR-116). Public like every reading on the
// Admin tab: a count holds no visitor, and the first screen shows the same figure through /api/pulse.
using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Visitors;

namespace TheYard.Api.Features.Visitors;

/// <summary>The Visitors slice's route.</summary>
public static class VisitorEndpoints
{
    /// <summary>Maps GET /api/admin/visitors/people.</summary>
    /// <param name="app">The host's routes.</param>
    public static IEndpointRouteBuilder MapVisitorEndpoints(this IEndpointRouteBuilder app)
    {
        // #region people-endpoint
        app.MapGet("/api/admin/visitors/people", People)
            .WithName("GetPeopleWhoHaveVisited")
            .WithTags("Admin")
            .WithSummary("People who have visited, all time, today and this month, the scanners left out, the last thirty days, and the kept visits since the first day, with the source");
        // #endregion people-endpoint
        return app;
    }

    private static async Task<Ok<PeopleCount>> People(ReadPeopleCountHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadPeopleCountQuery(), cancellation));
}
