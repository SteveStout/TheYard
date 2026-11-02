using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Gates;

/// <summary>
/// Holds a drafted text that speaks of an application when the tracker says that application is over, or has no such
/// application at all. "I applied for the X role" on a rejected req is the note a person had to stop by hand. A rejection
/// counts when it is dated on or after the application and names that req or its Count, or names no req at all: a generic
/// notice could be this one, and a block wrongly held waits a day. One that cites another Count is about that other
/// application. The tracker is what this reads, not the employer, so a closing nobody has logged still passes.
/// </summary>
public static partial class ClosedReqs
{
    private const int HeadLength = 200;

    /// <summary>Why this text may not go, or null.</summary>
    /// <param name="company">The company.</param>
    /// <param name="text">The drafted text.</param>
    /// <param name="rows">Tracker rows, master and spool, seven cells each.</param>
    public static string? Why(string company, string text, IReadOnlyList<string[]> rows)
    {
        Match claim = Claim().Match(text);
        if (!claim.Success)
        {
            return null;
        }
        string role = claim.Groups["role"].Value.Trim();
        string id = Req().Match(role).Groups["id"].Value;
        string title = Normal(Req().Replace(role, " "));
        bool Names(string[] row)
        {
            string cell = $" {Normal(row[2] + " " + row[6])} ";
            return (id.Length > 0 && cell.Contains($" {id.ToLowerInvariant()} ", StringComparison.Ordinal)) || cell.Contains($" {title} ", StringComparison.Ordinal);
        }
        var applications = rows.Where(row => IsApplicationAt(row, company) && Names(row)).ToList();
        if (applications.Count == 0)
        {
            return $"no application on file for {role}";
        }
        DateOnly applied = applications.Select(TrackerKinds.Day).OfType<DateOnly>().DefaultIfEmpty(DateOnly.MinValue).Max();
        string[]? closing = rows.FirstOrDefault(row => row.Length >= 7 && At(row, company) && Closes(row) && TrackerKinds.Day(row) is { } day && day >= applied
            && (Names(row) || applications.Any(application => Cites(row, application)) || !CitesACount().IsMatch(row[6])));
        return closing is null ? null : $"closed req: {role}, Count {closing[0]} {closing[4]}";
    }

    private static bool IsApplicationAt(string[] row, string company) =>
        TrackerRows.IsApplication(row) && !TrackerKinds.IsLinkedIn(row) && At(row, company) && !Closes(row) && !NotApplied().IsMatch(row[6]);

    private static bool At(string[] row, string company) => row[2].Split('/').Any(part => ApplicationCap.Same(part, company));

    private static bool Closes(string[] row)
    {
        string note = Bracket().Replace(row[6], string.Empty);
        return Closed().IsMatch(note[..Math.Min(HeadLength, note.Length)]);
    }

    private static bool Cites(string[] row, string[] application) =>
        application[0].Trim().Length > 0 && Regex.IsMatch(row[6], $@"\b{Regex.Escape(application[0].Trim())}\b");

    private static string Normal(string text) => Space().Replace(NotWord().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"\b(?:I applied for|I am in process for) the (?:Remote )?(?<role>\S.*?) role\b", RegexOptions.Singleline)]
    private static partial Regex Claim();

    [GeneratedRegex(@"\((?<id>[A-Za-z]*\d{4,})\)")]
    private static partial Regex Req();

    [GeneratedRegex(@"reject|not selected|no longer (being )?consider|position (has been )?(filled|closed)|posting closed|\b(req|requisition|role) (has been |is )?(closed|filled)|withdr[ae]w|not moving forward|move forward with other|no longer accepting", RegexOptions.IgnoreCase)]
    private static partial Regex Closed();

    [GeneratedRegex(@"\b(Counts?|rows?)\s+\d{3,5}", RegexOptions.IgnoreCase)]
    private static partial Regex CitesACount();

    [GeneratedRegex(@"^\W*NOT APPLIED", RegexOptions.IgnoreCase)]
    private static partial Regex NotApplied();

    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
