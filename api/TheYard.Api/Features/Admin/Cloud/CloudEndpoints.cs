using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The resource cards' route, a window of one resource from its keep, and the nightly card's run histories.</summary>
public static class CloudEndpoints
{
    /// <summary>Maps GET /api/admin/cloud/{key} and GET /api/admin/cloud/nightly/runs/{process}.</summary>
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
            .WithSummary("One resource under Cloud infrastructure: sqldb, cosmosdb, registry, insights, mail, edge or nightly, for 24h, 7d or 30d");
        // #endregion resource-endpoint
        // #region process-runs-endpoint
        // Public like the card: a run's when, how long, what it came to and its counts, nothing the card and
        // the Function's own health route do not already show. A name not among the six is a 404 problem, so
        // the route cannot be asked to read anything the processes' table does not name.
        app.MapGet("/api/admin/cloud/nightly/runs/{process}", Runs)
            .WithName("GetProcessRuns")
            .WithTags("Admin")
            .WithSummary("One page of one of the job hunt Function's six processes' runs, newest first, for 24h, 7d or 30d");
        // #endregion process-runs-endpoint
        return app;
    }

    private static async Task<Results<Ok<ProcessRunsPage>, ProblemHttpResult>> Runs(string process, string? window, int? page, ReadProcessRunsHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadProcessRunsQuery(process, window, page), cancellation) is { } runs
            ? TypedResults.Ok(runs)
            : TypedResults.Problem(detail: "The job hunt's Function has no process by that name; the six are AtsSweep, Merger, Nightly, SheetMirror, DailyCard and MergerHealth.", statusCode: 404, title: "No such process");

    private static async Task<Results<Ok<CloudReport>, NotFound>> Card(string key, string? window, ReadCloudCardHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadCloudCardQuery(key, window), cancellation) is { } report ? TypedResults.Ok(report) : TypedResults.NotFound();
}
