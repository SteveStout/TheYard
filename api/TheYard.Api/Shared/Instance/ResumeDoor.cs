namespace TheYard.Api.Shared.Instance;

// #region resume-door
/// <summary>
/// The resume at its own address, /resume, and the door a visitor came through
/// (ADR: The resume has an address). A link the owner hands out carries
/// <c>?src=linkedin</c>, <c>?src=email</c> and so on, so the activity card can
/// say which door recruiters open the resume from. The query string is never
/// recorded (ADR-071): the handler turns it into one word from a short allow
/// list, and the activity hook records that word as part of the path,
/// <c>/resume/linkedin</c>, so the row stays a path and nothing about the visitor.
/// </summary>
public static class ResumeDoor
{
    /// <summary>The doors a link may name. Anything else, or nothing, is <c>other</c>.</summary>
    public static readonly IReadOnlySet<string> Doors =
        new HashSet<string>(["linkedin", "application", "email", "signature", "post"], StringComparer.Ordinal);

    /// <summary>The key the handler leaves on the request for the activity hook to record instead of the path.</summary>
    public const string ItemKey = "activity-path";

    /// <summary>The prefix every door's path starts with, which the funnel's resume step counts.</summary>
    public const string PathPrefix = "/resume/";

    /// <summary>The name a browser saves the file under: his name and the locked resume's date, the way he names the file he sends.</summary>
    public static string FileName => $"StevenStoutResume_{LockDate:yyyy-MM-dd}.pdf";

    /// <summary>The day the served resume was locked; a new resume moves it with the file and its hash.</summary>
    public static readonly DateOnly LockDate = new(2026, 10, 24);

    /// <summary>The door a <c>src</c> value names: one of <see cref="Doors"/> in lower case, or <c>other</c>.</summary>
    public static string Of(string? src)
    {
        string word = (src ?? "").Trim().ToLowerInvariant();
        return Doors.Contains(word) ? word : "other";
    }

    /// <summary>The path the activity row carries for a visit through a door: <c>/resume/linkedin</c>.</summary>
    public static string PathFor(string? src) => PathPrefix + Of(src);
}
// #endregion resume-door
