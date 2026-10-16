using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Domain;

namespace TestProject.Controllers;

/// <summary>
/// Turns a refused request into an error reply in RFC 9457, the web standard for errors: a status,
/// a short title, the reason in one sentence and a trace id. It is the one place errors get their
/// shape, so no controller needs a try/catch.
/// </summary>
/// <remarks>
/// The app's own refusals carry their status; a path that leaves home is a 400; a request the web
/// server cut off keeps its status (413 past the size limit); the form reader's refusal is a 413
/// when it names a limit, else a 400; a file the disk will not let us touch (locked, read-only,
/// gone, a full disk) is a 403 or a 409 naming no path. Anything else is a real bug, a 500.
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
        // 1. Pick the status and title by the kind of exception, most specific first: a
        //    BadHttpRequestException is an IOException. A status of 0 means none of these.
        (int status, string title) = exception switch
        {
            ApiRefusalException problem => (problem.Status, problem.Title),
            PathRefusedException => (StatusCodes.Status400BadRequest, "The path was refused"),
            BadHttpRequestException bad => (bad.StatusCode, "The request was refused"),
            InvalidDataException invalid when NamesALimit(invalid) => (StatusCodes.Status413PayloadTooLarge, "The upload is too large"),
            InvalidDataException => (StatusCodes.Status400BadRequest, "The upload could not be read"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "The filesystem refused"),
            IOException => (StatusCodes.Status409Conflict, "The filesystem refused"),
            _ => (0, string.Empty),
        };

        // 2. Not one we know: pass it on, so a real bug still shows up as a 500.
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        // 3. Write the reply: the reason from ReasonFor, and the trace id the logs carry, so a
        //    failure a user reports can be found there.
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = ReasonFor(exception),
                Extensions = { ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier },
            },
        });
    }
    // #endregion handle

    /// <summary>
    /// The sentence a reply gives as its reason. The app's refusals are written for the reader
    /// and pass through; the disk's own messages carry the absolute path, so they are replaced.
    /// </summary>
    /// <param name="exception">The refusal being answered.</param>
    public static string ReasonFor(Exception exception) => exception switch
    {
        BadHttpRequestException bad => bad.Message,
        InvalidDataException invalid when NamesALimit(invalid) => "The upload is larger than the server accepts.",
        InvalidDataException => "The upload's body was cut short or is not a form the server can read.",
        UnauthorizedAccessException => "The disk refused: the file or folder is read-only or locked against changes.",
        IOException => "The disk refused: the file is in use by another program, it is gone, or the disk is full.",
        _ => exception.Message,
    };

    /// <summary>
    /// True when the form reader's refusal is about a size or count limit ("Multipart body length
    /// limit 1048576 exceeded"), which is the only InvalidDataException that means too large; the
    /// same type also reports a body cut short or a missing boundary.
    /// </summary>
    /// <param name="invalid">The form reader's refusal.</param>
    private static bool NamesALimit(InvalidDataException invalid) =>
        invalid.Message.Contains("limit", StringComparison.OrdinalIgnoreCase);
}
