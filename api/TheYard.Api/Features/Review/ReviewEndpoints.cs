using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Review;

namespace TheYard.Api.Features.Review;

/// <summary>
/// The review route's two routes (ADR: Ten minutes, seven stops, the addendum on the reach rows): the
/// page's report that a visitor reached a stop, and the Admin tab's card. The hash a reach is kept under
/// is the one the shared visitor tokens make (ADR-116); this file asks for it and never makes one.
/// </summary>
public static class ReviewEndpoints
{
    /// <summary>Maps POST /api/review/reached, GET /api/admin/review, GET /api/review/trend and GET /api/review/lot.</summary>
    /// <param name="app">The built host's routes.</param>
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        // #region review-endpoints
        // The page reports each stop reached once a browser, the route's opening as stop 0, and each button
        // pressed by its name. 204 for a stop on the route or a name on the list, 400 with the problem
        // otherwise; a code that is not one is dropped, not refused.
        app.MapPost("/api/review/reached", Reached)
            .WithName("RecordReviewReach")
            .WithTags("Review")
            .WithSummary("The page's report that a visitor reached a stop on the review route or pressed one of its buttons: how far, never who")
            .Accepts<ReviewReachedBody>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Public like the rest of the Admin tab (ADR: The Admin tab, as a product): counts and codes, never names.
        app.MapGet("/api/admin/review", Funnel)
            .WithName("GetReviewFunnel")
            .WithTags("Admin")
            .WithSummary("How far visitors got along the review route: opened, each stop reached, and by tagged code, for 24h, 7d, 30d or 90d");
        // The short path's two reads, on a page strangers open: the chart's readings, kept until the history file
        // changes, and one live lot a store, kept fifteen seconds and shared by every visitor in that window.
        app.MapGet("/api/review/trend", Trend)
            .WithName("GetReviewTrend")
            .WithTags("Review")
            .WithSummary("The quality number the Quality page charts first, by version, for the review route's short path")
            .Produces<ReviewTrend>(StatusCodes.Status200OK);

        app.MapGet("/api/review/lot", Lot)
            .WithName("GetReviewLot")
            .WithTags("Review")
            .WithSummary("One live lot for the review route's short path: the auction closing soonest with a bid, kept fifteen seconds")
            .Produces<ReviewLot>(StatusCodes.Status200OK);
        // #endregion review-endpoints
        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Reached(ReviewReachedBody body, HttpContext http, VisitorTokens tokens, RecordReviewReachHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new RecordReviewReachCommand(PersonOf(http, tokens), body.Stop, body.R, body.Action, body.Screen), cancellation)
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                detail: $"A report carries a stop, 0 (opened) to {ReviewFolding.LastStop}, or an action, one of {string.Join(", ", ReviewActions.Names)}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Not a stop or an action on the review route");

    private static async Task<Ok<ReviewTrend>> Trend(ReadReviewTrendHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadReviewTrendQuery(), cancellation));

    private static async Task<Ok<ReviewLot>> Lot(ReadReviewLotHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadReviewLotQuery(), cancellation));

    private static async Task<Ok<ReviewFunnel>> Funnel(string? window, ReadReviewFunnelHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadReviewFunnelQuery(ReviewWindows.Parse(window)), cancellation));

    /// <summary>
    /// The visitor's stable keyed hash, or null for a request the site does not count as a person: one its own
    /// tools make, the operator's (the cookie presenting the key set), the loopback address, or a scanner.
    /// </summary>
    private static string? PersonOf(HttpContext http, VisitorTokens tokens)
    {
        string agent = http.Request.Headers.UserAgent.ToString();
        string address = VisitorTokens.AddressOf(http);
        bool operatorsOwn = http.Request.Cookies.TryGetValue(Hits.OperatorCookie, out var mark) && mark == tokens.OperatorMark();
        bool notAPerson = Hits.IsSelf(agent) || operatorsOwn || Hits.IsLoopbackNetwork(VisitorTokens.NetworkOf(address)) || Hits.LooksLikeABot(agent, http.Request.Path);
        return notAPerson ? null : tokens.PersonFor(address, agent);
    }
}

/// <summary>The page's report: the stop reached or the button pressed, and the ?r= code it opened on.</summary>
/// <param name="Stop">0 for opened, 1 to 7 for the stop reached, or null for an action alone.</param>
/// <param name="R">The code, or null.</param>
/// <param name="Action">The button pressed, one of <see cref="ReviewActions.Names"/>, or null.</param>
/// <param name="Screen">The first screen the page drew, "verdict" or "mirror", or null.</param>
public sealed record ReviewReachedBody(int? Stop, string? R, string? Action = null, string? Screen = null);
