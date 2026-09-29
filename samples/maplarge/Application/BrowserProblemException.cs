namespace TestProject.Application;

/// <summary>
/// A use case refusing a request, with the status the API should answer and the
/// sentence a person should read (ADR-004). One exception type with a status
/// beats one type per status: the handler that turns it into a problem document
/// stays four lines, and a new refusal is a new factory here, not a new class
/// and a new mapping.
/// </summary>
public sealed class BrowserProblemException : Exception
{
    /// <summary>The HTTP status the API answers with.</summary>
    public int Status { get; }

    /// <summary>The short title the problem document carries.</summary>
    public string Title { get; }

    private BrowserProblemException(int status, string title, string detail) : base(detail)
    {
        Status = status;
        Title = title;
    }

    /// <summary>404: the path names nothing, or not the kind of thing the request needs.</summary>
    /// <param name="detail">Which path, and what was expected there.</param>
    public static BrowserProblemException NotFound(string detail) => new(404, "Not found", detail);

    /// <summary>400: the request is well formed but asks for something the rules refuse.</summary>
    /// <param name="detail">What the rule is.</param>
    public static BrowserProblemException Refused(string detail) => new(400, "The request was refused", detail);

    /// <summary>409: something is already at the destination.</summary>
    /// <param name="detail">What is in the way.</param>
    public static BrowserProblemException Conflict(string detail) => new(409, "Already exists", detail);

    /// <summary>413: an upload is larger than the configured limit.</summary>
    /// <param name="detail">The file and the limit.</param>
    public static BrowserProblemException TooLarge(string detail) => new(413, "Too large", detail);
}
