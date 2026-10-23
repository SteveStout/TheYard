using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Links;

namespace TheYard.Api.Features.Links;

/// <summary>
/// The Links slice's routes (ADR: Tagged links, who opened what): the pass-through to GitHub and
/// LinkedIn, the page's own report of its open, and the Admin tab's card. The open itself is
/// recorded by the request recorder, which sees every tagged request; nothing here writes.
/// </summary>
public static class LinkEndpoints
{
    /// <summary>Maps GET /api/go/{key}, GET /api/links/open and GET /api/admin/links.</summary>
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // #region link-endpoints
        // A link to GitHub or LinkedIn goes through here so the open is counted, then on to the
        // address configuration names for the key. A key the configuration does not name is 404:
        // the route cannot be made to send anyone anywhere else.
        app.MapGet("/api/go/{key}", Go)
            .WithName("GoThrough")
            .WithTags("Links")
            .WithSummary("Counts the open of a tagged link and sends the visitor on to GitHub or LinkedIn")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // The page reports its own open: it is served by the rendering service, so a code on its
        // address reaches this process only when the page says so. 204 for a code and a view the
        // page has, 400 otherwise; the page learns nothing either way.
        app.MapGet("/api/links/open", PageOpen)
            .WithName("ReportPageOpen")
            .WithTags("Links")
            .WithSummary("The page's own report that a tagged link opened it")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Public like the rest of the Admin tab (ADR: The Admin tab, as a product): codes, never
        // names; who a code was sent to is the operator's own lookup, off the site.
        app.MapGet("/api/admin/links", Opens)
            .WithName("GetLinkOpens")
            .WithTags("Admin")
            .WithSummary("Who opened what, by code: each tagged link's first and last open, count and paths, for 7d, 30d or 90d");
        // #endregion link-endpoints
        return app;
    }

    private static async Task<Results<RedirectHttpResult, NotFound>> Go(string key, GoHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new GoQuery(key), cancellation) is { } target ? TypedResults.Redirect(target.ToString(), permanent: false) : TypedResults.NotFound();

    private static async Task<Results<NoContent, BadRequest>> PageOpen(string? r, string? path, ReportPageOpenHandler handler, CancellationToken cancellation) =>
        await handler.HandleAsync(new ReportPageOpenCommand(r, path), cancellation) ? TypedResults.NoContent() : TypedResults.BadRequest();

    private static async Task<Ok<LinkOpensReport>> Opens(string? window, ReadLinkOpensHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadLinkOpensQuery(window), cancellation));
}
