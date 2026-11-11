using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The resource cards' route, a window of one resource from its keep, the nightly card's run histories, and the one address a card is fed through.</summary>
public static class CloudEndpoints
{
    /// <summary>Maps GET /api/admin/cloud/{key}, POST /api/admin/cloud/nightly/run, the box's two posts and its live read, GET /api/admin/cloud/nightly/runs/{process}, GET /api/admin/incidents, GET /api/admin/funnel, GET /api/admin/pounce and GET /api/admin/channels.</summary>
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
            .WithSummary("One resource under Cloud infrastructure: sqldb, cosmosdb, registry, insights, share, blob, mail, edge, nightly, claude, campaign, network, box or picker, for 24h, 7d or 30d");
        // #endregion resource-endpoint
        // #region process-runs-endpoint
        // Public like the card: a run's when, how long, what it came to and its counts, nothing the card and
        // the Function's own health route do not already show. A name not among the eight is a 404 problem, so
        // the route cannot be asked to read anything the processes' table does not name.
        app.MapGet("/api/admin/cloud/nightly/runs/{process}", Runs)
            .WithName("GetProcessRuns")
            .WithTags("Admin")
            .WithSummary("One page of one of the job hunt Function's eight processes' runs, newest first, for 24h, 7d or 30d");
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
        // #region box-endpoints
        // The box that supports the job hunt (ADR: The box reports itself): its agent posts a snapshot a minute and a
        // live sample every two seconds behind the operator's key, and the card's live panels read the ring back,
        // anonymous like every card. The two writes declare each status they answer, like the nightly run's.
        app.MapPost("/api/admin/cloud/box/snapshot", BoxRoutes.SnapshotPost)
            .WithName("PostBoxSnapshot")
            .WithTags("Admin")
            .WithSummary("Hold one snapshot of the box, behind the operator's key")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        app.MapPost("/api/admin/cloud/box/live", BoxRoutes.LivePost)
            .WithName("PostBoxLiveSample")
            .WithTags("Admin")
            .WithSummary("Add one live sample of the box to the ring, behind the operator's key")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        app.MapGet("/api/admin/cloud/box/live", BoxRoutes.LiveGet)
            .WithName("GetBoxLive")
            .WithTags("Admin")
            .WithSummary("The box's last minute of live samples and how long it has been silent");
        // Every card's live ring (ADR: Live dials where the source allows): a read leases the card's source for a minute,
        // anonymous like the card itself, and a card with no live source answers available false with its sentence.
        // The box keeps its own route above, which the literal path wins over this one.
        app.MapGet("/api/admin/cloud/{key}/live", Live)
            .WithName("GetCardLive")
            .WithTags("Admin")
            .WithSummary("One card's live ring: its source, its grain and the last ten minutes, or why it has none");
        // #endregion box-endpoints
        // #region incidents-endpoint
        // Public like the cards: the incidents are public pages, and a failure is named by its type and where it
        // happened, never by its message, which can carry what a visitor sent.
        app.MapGet("/api/admin/incidents", Incidents)
            .WithName("GetIncidents")
            .WithTags("Admin")
            .WithSummary("What broke: every incident the docs keep, and the exceptions and 5xx answers Application Insights kept in the last 7 days");
        // #endregion incidents-endpoint
        // #region funnel-endpoint
        // Public like the cards: six counts a day and a tone, nothing that names a company or a person (ADR-109, the
        // addendum on the funnel). Read from the Function's own line; nothing is typed here.
        app.MapGet("/api/admin/funnel", Funnel)
            .WithName("GetFunnel")
            .WithTags("Admin")
            .WithSummary("The job hunt's funnel from the Function's daily line: scanned, READY, applied, people reached, replies and calls booked, for 24h, 7d or 30d, graded on the week's calls");
        // #endregion funnel-endpoint
        // #region pounce-endpoint
        // Public like the funnel: six counts a Central day and a judgement, nothing that names a company, a title or a
        // person (ADR-109, the addendum on time to first touch). Read from the Function's own lines; nothing is typed here.
        app.MapGet("/api/admin/pounce", Pounce)
            .WithName("GetPounce")
            .WithTags("Admin")
            .WithSummary("Time to first touch from the Function's PounceDay lines: for each of the last seven Central days, roles caught, touched, the median and worst minutes to the first message, and the ones still waiting, against 60 minutes");
        // #endregion pounce-endpoint
        // #region channels-endpoint
        // Public like the funnel: counts a channel a week, nothing that names a company, a title or a person (ADR-109,
        // the addendum on the funnel by channel). Read by the owner alone: no message ever links the Admin tab.
        app.MapGet("/api/admin/channels", Channels)
            .WithName("GetChannels")
            .WithTags("Admin")
            .WithSummary("The job hunt's funnel by channel from the card schema's Channels view: for each week, newest first, the touches, replies and rooms booked of pounce, hotmgr, the email campaign, old contacts, applications and recruiters");
        // #endregion channels-endpoint
        return app;
    }

    private static async Task<Results<Ok<ProcessRunsPage>, ProblemHttpResult>> Runs(string process, string? window, int? page, ReadProcessRunsHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadProcessRunsQuery(process, window, page), cancellation) is { } runs
            ? TypedResults.Ok(runs)
            : TypedResults.Problem(detail: "The job hunt's Function has no process by that name; the eight are AtsSweep, Merger, Nightly, SheetMirror, DailyCard, CampaignSend, NetworkLogPass and MergerHealth.", statusCode: 404, title: "No such process");

    private static async Task<IResult> Live(string key, ReadLiveHandler handler, CancellationToken cancellation) =>
        Results.Json(await handler.HandleAsync(new ReadLiveQuery(key), cancellation), LiveWire);

    /// <summary>The live wire, snake_case like every Admin read.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions LiveWire = new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };

    private static async Task<Ok<FunnelReport>> Funnel(string? window, ReadFunnelHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadFunnelQuery(window), cancellation));

    private static async Task<Ok<PounceReport>> Pounce(ReadPounceHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadPounceQuery(), cancellation));

    private static async Task<Ok<ChannelsReport>> Channels(ReadChannelsHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadChannelsQuery(), cancellation));

    private static async Task<Ok<IncidentsReport>> Incidents(ReadIncidentsHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadIncidentsQuery(), cancellation));

    private static async Task<Results<Ok<CloudReport>, NotFound>> Card(string key, string? window, ReadCloudCardHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReadCloudCardQuery(key, window), cancellation) is { } report ? TypedResults.Ok(report) : TypedResults.NotFound();
}
