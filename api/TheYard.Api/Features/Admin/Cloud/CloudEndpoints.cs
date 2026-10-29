using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The resource cards' route, a window of one resource from its keep, the nightly card's run histories, and the one address a card is fed through.</summary>
public static class CloudEndpoints
{
    /// <summary>Maps GET /api/admin/cloud/{key}, POST /api/admin/cloud/nightly/run, GET /api/admin/cloud/nightly/runs/{process} and GET /api/admin/incidents.</summary>
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
            .WithSummary("One resource under Cloud infrastructure: sqldb, cosmosdb, registry, insights, share, blob, mail, edge, nightly, claude or campaign, for 24h, 7d or 30d");
        // #endregion resource-endpoint
        // #region process-runs-endpoint
        // Public like the card: a run's when, how long, what it came to and its counts, nothing the card and
        // the Function's own health route do not already show. A name not among the seven is a 404 problem, so
        // the route cannot be asked to read anything the processes' table does not name.
        app.MapGet("/api/admin/cloud/nightly/runs/{process}", Runs)
            .WithName("GetProcessRuns")
            .WithTags("Admin")
            .WithSummary("One page of one of the job hunt Function's seven processes' runs, newest first, for 24h, 7d or 30d");
        // #endregion process-runs-endpoint
        // #region nightly-run-endpoint
        // One run of the nightly workflow, posted by the workflow that ran it (ADR: The Nightly
        // Function on the Admin tab), behind the operator's key in its header. This is what
        // retires the card's hourly read of GitHub and its sixty-an-hour
        // allowance: the runs arrive as they end, and GitHub is asked only to reconcile. A run
        // posted twice is kept once, under GitHub's own id, so a workflow may post again after a
        // failure without the card counting the run twice. Unlike every other address on this tab
        // it is a write, so it declares each status it answers and each one has a case.
        app.MapPost("/api/admin/cloud/nightly/run", NightlyRunRoutes.RunPost)
            .WithName("PostNightlyRun")
            .WithTags("Admin")
            .WithSummary("Keep one run of the nightly workflow, under GitHub's own run id, behind the operator's key")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        // #endregion nightly-run-endpoint
        // #region incidents-endpoint
        // Public like the cards: the incidents are public pages, and a failure is named by its type and where it
        // happened, never by its message, which can carry what a visitor sent.
        app.MapGet("/api/admin/incidents", Incidents)
            .WithName("GetIncidents")
            .WithTags("Admin")
            .WithSummary("What broke: every incident the docs keep, and the exceptions and 5xx answers Application Insights kept in the last 7 days");
        // #endregion incidents-endpoint
        return app;
    }

    private static async Task<Results<Ok<ProcessRunsPage>, ProblemHttpResult>> Runs(string process, string? window, int? page, ReadProcessRunsHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadProcessRunsQuery(process, window, page), cancellation) is { } runs
            ? TypedResults.Ok(runs)
            : TypedResults.Problem(detail: "The job hunt's Function has no process by that name; the seven are AtsSweep, Merger, Nightly, SheetMirror, DailyCard, CampaignSend and MergerHealth.", statusCode: 404, title: "No such process");

    private static async Task<Ok<IncidentsReport>> Incidents(ReadIncidentsHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadIncidentsQuery(), cancellation));

    private static async Task<Results<Ok<CloudReport>, NotFound>> Card(string key, string? window, ReadCloudCardHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadCloudCardQuery(key, window), cancellation) is { } report ? TypedResults.Ok(report) : TypedResults.NotFound();
}
