using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TheBlock.Api;

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
    public const string ServerDetail =
        "Something went wrong on the server. Quote the trace id below and the "
        + "request can be found in the logs.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The caller hung up mid-response. There is no socket left to answer
        // on and nothing went wrong here, so this is a debug line and not an
        // error, and it must not reach the ring buffer as a 500.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            log.LogDebug(
                "{Method} {Path} was abandoned by the caller",
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
        return await problems.TryWriteAsync(new ProblemDetailsContext
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
    }
    // #endregion exception-handler
}
