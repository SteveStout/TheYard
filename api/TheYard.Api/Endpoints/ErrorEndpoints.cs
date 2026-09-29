using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>
/// Recent errors from the server and the browser, merged for the Admin tab; the
/// page's own report of an error it caught; and the failure on purpose that
/// proves the error path works on the live container.
/// </summary>
public static class ErrorEndpoints
{
    /// <summary>Maps /api/errors, /api/errors/client and the self-test.</summary>
    public static IEndpointRouteBuilder MapErrorEndpoints(this IEndpointRouteBuilder app)
    {
        // Both rings, merged newest first, so the page is still one list.
        app.MapGet("/api/errors", Recent)
            .WithName("GetErrors")
            .WithTags("Errors")
            .WithSummary("Recent server and browser errors, newest first")
            .WithDescription("Two in-memory rings of fifty, merged. Exception types and stack frames, never messages: this list is public.");

        #region client-errors
        // Browser errors land where server errors already do (ADR-023): a render
        // crash caught by the boundary, or an unhandled rejection, POSTs here and
        // shows up on the Admin tab's Recent errors card tagged with the page the
        // visitor was on. Status 0 marks the entry as coming from the browser.
        app.MapPost("/api/errors/client", BrowserReport)
            .WithName("ReportBrowserError")
            .WithTags("Errors")
            .WithSummary("Report an error the browser caught")
            .WithDescription("Anonymous by design, so a crash in the page reaches the same list a crash in the server does. "
                + "The message and the stack are bounded and any address in them is masked before they are kept.")
            .ProducesProblem(StatusCodes.Status400BadRequest);
        #endregion client-errors

        #region selftest
        // A failure on purpose, in production, because every other endpoint here is
        // written not to throw and so the exception path had never once run against
        // the live container: not the middleware's catch, not the ring buffer's
        // record, not the Application Insights exceptions the Admin tab reads. This
        // asks all three at once, and the answer it produces is the answer any real
        // bug would produce (ADR-030).
        app.MapGet("/api/admin/selftest/exception", SelfTest);
        #endregion selftest

        return app;
    }

    private static Ok<ErrorEntry[]> Recent(ErrorRings rings) =>
        TypedResults.Ok(
        rings.Server.Snapshot()
            .Concat(rings.Browser.Snapshot())
            .OrderByDescending(entry => entry.At)
            .ToArray());

    private static Results<NoContent, ProblemHttpResult> BrowserReport(ClientErrorReport report, ILoggerFactory loggers, ErrorRings rings)
    {
        if (string.IsNullOrWhiteSpace(report.Message))
        {
            return TypedResults.Problem(detail: "A client error report needs a message.", statusCode: 400,
                title: "The error report could not be read");
        }
        // Bounded, and with any at sign encoded: the message and the stack come
        // from an unauthenticated POST and end up on a public page, in Application
        // Insights and now in the kept log, and a browser report that quotes an
        // address must not become a public line that names it (ADR: Logs that
        // outlive the container). The stack was not bounded here at first, only
        // the message was (the staff review, 2026-09-03).
        string message = LogText.Clean(report.Message, LogText.MessageLength);
        string stack = LogText.Clean(report.Stack, LogText.DetailLength);
        string where = string.IsNullOrWhiteSpace(report.Path) ? "(browser)" : report.Path;
        rings.Browser.Record(where, 0, "browser: " + message);
        // The same report goes to Application Insights as a structured log, so a
        // browser error is searchable beside the server's own (ADR-024). Logging
        // rather than posting from the browser keeps the page free of a second
        // external script and keeps the ingestion key server-side.
        loggers.CreateLogger("TheYard.Browser").LogError(
            "Browser error on {Path}: {BrowserMessage} {BrowserStack}", where, message, stack);
        return TypedResults.NoContent();
    }

    private static IResult SelfTest() =>
        throw new InvalidOperationException(
            "Deliberate self-test failure. No caller ever sees this sentence, which "
            + "is the point of it: it exists to be found in a log and nowhere else.");
}

/// <summary>What the browser reports when a render crashes or a promise rejects (ADR-023).</summary>
public sealed record ClientErrorReport(
    [property: Description("What the browser caught. Required; bounded and masked before it is kept.")] string? Message,
    [property: Description("The stack, if there was one.")] string? Stack,
    [property: Description("The page the visitor was on.")] string? Path);
