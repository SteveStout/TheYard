using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The resource cards' one route: a window of one resource, from its keep.</summary>
public static class CloudEndpoints
{
    /// <summary>Maps GET /api/admin/cloud/{key}.</summary>
    public static IEndpointRouteBuilder MapCloudEndpoints(this IEndpointRouteBuilder app)
    {
        // #region resource-endpoint
        // Public like the plan's card: a resource's load is no more private than the plan's.
        // Answered from the card's keep; Azure or Netlify is asked only when the newest kept
        // reading is stale, and only on the SQL site, where the recorder runs. A key no card
        // has is a 404, so the route cannot be asked to read anything the catalogue does not name.
        app.MapGet("/api/admin/cloud/{key}", Card)
            .WithName("GetResourceCard")
            .WithTags("Admin")
            .WithSummary("One resource under Cloud infrastructure: sqldb, cosmosdb, registry, insights, mail or edge, for 24h, 7d or 30d");
        // #endregion resource-endpoint
        return app;
    }

    private static async Task<Results<Ok<CloudReport>, NotFound>> Card(string key, string? window, CloudReader reader, CancellationToken cancellation) =>
        reader.Knows(key) ? TypedResults.Ok(await reader.ReadAsync(key, window, cancellation)) : TypedResults.NotFound();
}
