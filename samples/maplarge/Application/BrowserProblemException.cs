namespace TestProject.Application;

/// <summary>
/// Thrown when a file operation refuses a request. It carries the HTTP status the API
/// should answer with, a short title, and a sentence a person can read, which becomes
/// the exception message. One exception type carrying its own status is used instead
/// of one type per status: the handler that turns it into a problem response stays a
/// few lines long, and a new kind of refusal only needs a new factory method here, not
/// a new class and a new mapping in the handler.
/// </summary>
public sealed class BrowserProblemException : Exception
{
    /// <summary>The HTTP status code the API answers with.</summary>
    public int Status { get; }

    /// <summary>The short title placed in the problem response the API sends.</summary>
    public string Title { get; }

    private BrowserProblemException(int status, string title, string detail) : base(detail)
    {
        Status = status;
        Title = title;
    }

    /// <summary>404: the path names nothing, or the wrong kind of thing for the request.</summary>
    /// <param name="detail">Which path, and what is expected there.</param>
    public static BrowserProblemException NotFound(string detail) => new(404, "Not found", detail);

    /// <summary>400: the request is well formed but asks for something the rules forbid.</summary>
    /// <param name="detail">Which rule the request broke.</param>
    public static BrowserProblemException Refused(string detail) => new(400, "The request was refused", detail);

    /// <summary>409: something already exists at the destination.</summary>
    /// <param name="detail">What is already there.</param>
    public static BrowserProblemException Conflict(string detail) => new(409, "Already exists", detail);

    /// <summary>413: an upload is larger than the configured limit.</summary>
    /// <param name="detail">The file's name and size, and the limit.</param>
    public static BrowserProblemException TooLarge(string detail) => new(413, "Too large", detail);
}
