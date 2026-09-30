using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Domain;

namespace TestProject.Controllers;

/// <summary>
/// Turns a refused request into an RFC 9457 problem document. A
/// <see cref="BrowserProblemException"/> carries its own status and title. A
/// <see cref="PathRefusedException"/> from the home directory check is always a 400.
/// Refusals from the filesystem itself, such as a read-only file inside a folder being
/// deleted or a file another process holds open, become a 403 or a 409 carrying the
/// operating system's message. They are treated as client errors because the request
/// asked for something the disk cannot do, not because the server is broken.
/// Any other exception is left unhandled here, so a real bug still reaches the default
/// handler as a 500 with a trace id and is never reported as a client error.
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
                // Adds the same trace id the logs carry, so a reported failure can be found there.
                Extensions = { ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier },
            },
        });
    }
    // #endregion handle
}
