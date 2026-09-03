using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TheYard.Api;

/// <summary>
/// What a caller is told when something fails that no endpoint expected
/// (ADR: The exception handler).
///
/// The built-in <c>UseExceptionHandler</c> with <c>AddProblemDetails</c>
/// already produces a bare 500 in the right shape. This adds the three things
/// it cannot decide for an application: which failures are the caller's fault
/// and should say so, what a server failure is allowed to reveal, and what
/// gets written down so the trace id in the response is worth returning.
/// </summary>
public sealed class ProblemHandler(IProblemDetailsService problems, ILogger<ProblemHandler> log)
    : IExceptionHandler
{
    // #region exception-handler
    /// <summary>
    /// The sentence a caller gets for a server failure. It is deliberately the
    /// same every time: an error message that varies with the exception is a
    /// map of the inside of the process, drawn for whoever asks.
    /// </summary>
    /// <summary>
    /// Not an HTTP standard, and the standard has nothing for this. nginx uses
    /// 499 for a client that closed the connection before an answer, enough
    /// tooling understands it, and the alternative is leaving a 200 on a
    /// request that was never answered.
    /// </summary>
    public const int ClientClosedRequest = 499;

    public const string ServerDetail =
        "Something went wrong on the server. Quote the trace id below and the "
        + "request can be found in the logs.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The caller hung up mid-response. There is no socket left to answer
        // on, so nothing is written. It is still recorded, at Information and
        // with a status that says what happened: the first shape of a real
        // outage is clients timing out and aborting, and a version of this that
        // logged at Debug and left the status at 200 would have made that
        // outage invisible in the request log and in telemetry alike (the staff
        // review, 2026-09-03). 499 is nginx's convention for it and is what
        // shows up in a log as "the caller left", not "we answered".
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
            log.LogInformation(
                "{Method} {Path} was abandoned by the caller; nothing was written",
                context.Request.Method,
                context.Request.Path);
            return true;
        }

        // BadHttpRequestException is the framework saying the request was
        // malformed, which is a different claim from "this server broke". Its
        // message describes the caller's own input, so passing it on is both
        // safe and the only useful thing to say.
        bool callersFault = exception is BadHttpRequestException;
        int status = callersFault
            ? ((BadHttpRequestException)exception).StatusCode
            : StatusCodes.Status500InternalServerError;

        // One line, before the response is written, carrying the trace id the
        // caller is about to be handed. This is the whole reason the response
        // can afford to say nothing: everything it withholds is here.
        log.Log(
            callersFault ? LogLevel.Warning : LogLevel.Error,
            exception,
            "{Exception} on {Method} {Path} answered {Status}, trace {TraceId}",
            exception.GetType().Name,
            context.Request.Method,
            context.Request.Path,
            status,
            context.TraceIdentifier);

        context.Response.StatusCode = status;
        // The same writer every deliberate Results.Problem call goes through,
        // so a crash and a rejected query are one shape on the wire. The
        // traceId extension is added by the customization in Program.cs, which
        // means it is on this response too without being repeated here.
        //
        // It can decline. A caller who accepts only text/plain gets no
        // ProblemDetails, TryWriteAsync returns false, and the middleware
        // rethrows, which is the right outcome and not the one the log line
        // above describes. So the log says what was actually sent (the staff
        // review, 2026-09-03).
        bool written = await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = callersFault
                    ? "The request could not be read"
                    : "The request could not be completed",
                Detail = callersFault ? exception.Message : ServerDetail,
            },
        });

        if (!written)
        {
            log.LogWarning(
                "Nothing was written for {Exception} on {Method} {Path}: no problem details writer accepted it, so the request will be reset",
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path);
        }

        return written;
    }
    // #endregion exception-handler
}
