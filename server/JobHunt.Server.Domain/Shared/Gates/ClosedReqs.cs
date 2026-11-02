using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Gates;

/// <summary>
/// Holds a drafted text that speaks of an application when the tracker says that application is over, or has no such
/// application at all. "I applied for the X role" on a rejected req is the note a person had to stop by hand. A rejection
/// counts when it is dated on or after the application and names that req or its Count, or names no req at all: a generic
/// notice could be this one, and a block wrongly held waits a day. One that cites another Count is about that other
/// application. A text with a req id is matched by the id, because two reqs often share a title, and the dates run from the
/// first application on that req, so a later row cannot hide an earlier rejection. A text that speaks of applying in words
/// this cannot read is held, because a claim nobody checked is the thing this is here to stop. The tracker is what this
/// reads, not the employer, so a closing nobody has logged still passes. A name in the parenthesis of an application's
/// company cell counts as the company, because the tracker files one employer under both.
/// </summary>
public static partial class ClosedReqs
{
    private const int HeadLength = 200;
    private const int OpeningLength = 80;

    /// <summary>Why this text may not go, or null.</summary>
    /// <param name="company">The company.</param>
    /// <param name="text">The drafted text.</param>
    /// <param name="rows">Tracker rows, master and spool, seven cells each.</param>
    public static string? Why(string company, string text, IReadOnlyList<string[]> rows)
    {
        Match claim = Claim().Match(text);
        if (!claim.Success)
        {
            return Speaks().IsMatch(text) ? "the text speaks of an application in words the gate cannot read" : null;
        }
        string role = claim.Groups["role"].Value.Trim();
        string id = Req().Match(role).Groups["id"].Value;
        string title = Normal(Req().Replace(role, " "));
        bool Names(string[] row)
        {
            string cell = $" {Normal(row[2] + " " + row[6])} ";
            return cell.Contains($" {(id.Length > 0 ? id.ToLowerInvariant() : title)} ", StringComparison.Ordinal);
        }
        var applications = rows.Where(row => IsApplicationAt(row, company) && Names(row)).ToList();
        if (applications.Count == 0)
        {
            return $"no application on file for {role}";
        }
        string[] names = [company, .. applications.SelectMany(row => Aliases(row[2]))];
        DateOnly applied = applications.Select(TrackerKinds.Day).OfType<DateOnly>().DefaultIfEmpty(DateOnly.MinValue).Min();
        string[]? closing = rows.FirstOrDefault(row => row.Length >= 7 && names.Any(name => At(row, name)) && Closes(row) && TrackerKinds.Day(row) is { } day && day >= applied
            && (Names(row) || applications.Any(application => Cites(row, application)) || !CitesACount().IsMatch(row[6])));
        return closing is null ? null : $"closed req: {role}, Count {closing[0]} {closing[4]}";
    }

    private static bool IsApplicationAt(string[] row, string company) =>
        TrackerRows.IsApplication(row) && !TrackerKinds.IsLinkedIn(row) && At(row, company) && !Closes(row) && !NeverWentIn(row);

    private static IEnumerable<string> Aliases(string cell) =>
        Alias().Matches(cell).Select(match => match.Groups[1].Value);

    private static bool At(string[] row, string company) => row[2].Split('/').Any(part => ApplicationCap.Same(part, company));

    private static bool Closes(string[] row)
    {
        string note = Bracket().Replace(row[6], string.Empty);
        return Closed().IsMatch(note[..Math.Min(HeadLength, note.Length)]);
    }

    private static bool NeverWentIn(string[] row)
    {
        string note = Bracket().Replace(row[6], string.Empty);
        return NotApplied().IsMatch(note[..Math.Min(OpeningLength, note.Length)]);
    }

    private static bool Cites(string[] row, string[] application) =>
        application[0].Trim().Length > 0 && Regex.IsMatch(row[6], $@"\b{Regex.Escape(application[0].Trim())}\b");

    /// <summary>A title or a note reduced to lower-case words, so two wordings of one title compare.</summary>
    /// <param name="text">The text.</param>
    public static string Normal(string text) => Space().Replace(NotWord().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"\b(?:applied(?:\s+on\s+[^.]*?)?\s+(?:for|to)|in process for)\s+the\s+(?:Remote\s+)?(?<role>\S.*?)\s+role\b", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Claim();

    [GeneratedRegex(@"\b(applied|applying|application|in process|role|req|requisition|position|opening)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Speaks();

    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex Alias();

    [GeneratedRegex(@"\((?<id>[A-Za-z]*\d{4,})\)")]
    private static partial Regex Req();

    [GeneratedRegex(@"reject|\bclosed\b|not selected|no longer (being )?consider|position (has been )?(filled|closed)|posting closed|\b(req|requisition|role) (has been |is )?(closed|filled)|withdr[ae]w|not moving forward|move forward with other|no longer accepting", RegexOptions.IgnoreCase)]
    private static partial Regex Closed();

    [GeneratedRegex(@"\b(Counts?|rows?)\s+\d{3,5}", RegexOptions.IgnoreCase)]
    private static partial Regex CitesACount();

    [GeneratedRegex(@"\b(NOT APPLIED|NOT SUBMITTED|QUEUED|STAGED|PARKED|SKIPPED|DROPPED)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotApplied();

    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
