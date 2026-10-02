using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Domain;

namespace TestProject.Controllers;

/// <summary>
/// Turns a refused request into a clear error reply in the RFC 9457 format, the web standard
/// for errors: a status code, a short title, the reason in one sentence, and a trace id. This
/// is the one place errors get their shape, so no controller needs a try/catch.
/// </summary>
/// <remarks>
/// What each kind of failure becomes, and why:
/// - The app's own refusals (ApiRefusalException) carry their status and title with them.
/// - A path that tries to leave the home folder (PathRefusedException) is always a 400.
/// - A file the disk will not let us touch, because it is read-only or another program has it
///   open, is a 403 or a 409 with the operating system's own message. The request asked for
///   something the disk cannot do; the server is not broken.
/// - Anything else is a real bug. It is left alone here, so it still reaches the default
///   handler as a 500 and is never passed off as the caller's mistake.
/// </remarks>
public sealed class ProblemResponseHandler(IProblemDetailsService problems) : IExceptionHandler
{
    // #region handle
    /// <summary>
    /// Called by ASP.NET Core when a request throws. Returns true when it wrote the error reply,
    /// or false to pass the exception on to the next handler.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // 1. Pick the status code and title from the kind of exception. A status of 0 means
        //    this is not a refusal this handler knows.
        (int status, string title) = exception switch
        {
            ApiRefusalException problem => (problem.Status, problem.Title),
            PathRefusedException => (StatusCodes.Status400BadRequest, "The path was refused"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "The filesystem refused"),
            IOException => (StatusCodes.Status409Conflict, "The filesystem refused"),
            _ => (0, string.Empty),
        };

        // 2. Not one we know: pass it on, so a real bug still shows up as a 500.
        if (status == 0)
        {
            return false;
        }

        // 3. Set the status code on the response.
        httpContext.Response.StatusCode = status;

        // 4. Write the error reply. The reason is the exception's own sentence, and the trace id
        //    is the same one the logs carry, so a failure a user reports can be found there.
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Extensions = { ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier },
            },
        });
    }
    // #endregion handle
}
