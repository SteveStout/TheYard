using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Domain;

namespace TestProject.Controllers;

/// <summary>
/// Turns a refusal into an RFC 9457 problem document (ADR-004). A
/// <see cref="BrowserProblemException"/> carries its own status and title; a
/// <see cref="PathRefusedException"/> from the home is always a 400; the
/// filesystem's own refusals (a read-only file inside a folder being deleted, a
/// file another process holds open) are a 409 or a 403 with the operating
/// system's sentence, because they are the request's problem, not the server's.
/// Anything else is not handled here, so an actual bug still reaches the default
/// handler as a 500 with a trace id and never masquerades as a client error.
/// </summary>
public sealed class BrowserProblemHandler(IProblemDetailsService problems) : IExceptionHandler
{
    // #region handle
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title) = exception switch
        {
            BrowserProblemException problem => (problem.Status, problem.Title),
            PathRefusedException => (StatusCodes.Status400BadRequest, "The path was refused"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "The filesystem refused"),
            IOException => (StatusCodes.Status409Conflict, "The filesystem refused"),
            _ => (0, string.Empty),
        };
        if (status == 0)
        {
            return false;
        }
        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                // The trace id the logs carry, so a person quoting a failure can be found in them.
                Extensions = { ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier },
            },
        });
    }
    // #endregion handle
}
