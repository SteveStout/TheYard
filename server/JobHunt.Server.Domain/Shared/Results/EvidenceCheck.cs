using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Results;

/// <summary>
/// Holds a claimed send to its evidence. The model's word is not a verification: an invite needs Pending, a toast
/// naming the person and the note read back; a message needs the sent bubble read back with its link and its blank
/// lines; an application needs the confirmation page. Each also needs a saved screenshot. A claim that fails is still
/// recorded, marked UNVERIFIED, because a send that happened and went unrecorded is how a second touch goes out.
/// </summary>
public static partial class EvidenceCheck
{
    /// <summary>Everything missing from a claimed send, empty when it stands.</summary>
    /// <param name="action">The worklist row's action.</param>
    /// <param name="expected">The text the row should have sent with its placeholders filled, empty for an application.</param>
    /// <param name="result">What the model reported.</param>
    public static IReadOnlyList<string> Missing(WorkAction action, string expected, RowResult result)
    {
        RowEvidence? evidence = result.Evidence;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(evidence?.Screenshot))
        {
            missing.Add("no screenshot");
        }
        switch (action)
        {
            case WorkAction.Invite:
                if (evidence?.Pending != true)
                {
                    missing.Add("button not read as Pending");
                }
                string first = (result.Person ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                if (first.Length == 0 || evidence?.Toast?.Contains(first, StringComparison.OrdinalIgnoreCase) != true)
                {
                    missing.Add("toast did not name the person");
                }
                if (!Same(expected, evidence?.ReadBack))
                {
                    missing.Add("note read back differs from the worklist text");
                }
                break;
            case WorkAction.Message:
                if (!SameLines(expected, evidence?.ReadBack))
                {
                    missing.Add("sent bubble differs from the worklist text");
                }
                break;
            default:
                if (string.IsNullOrWhiteSpace(evidence?.Confirmation))
                {
                    missing.Add("no confirmation page");
                }
                break;
        }
        return missing;
    }

    /// <summary>The text with a found name in place of [First name].</summary>
    /// <param name="text">The worklist text.</param>
    /// <param name="person">The person the lane reported.</param>
    public static string Filled(string text, string? person)
    {
        string first = (person ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.Length == 0 ? text : text.Replace("[First name]", first, StringComparison.Ordinal);
    }

    private static bool Same(string expected, string? actual) =>
        actual is not null && Space().Replace(expected, " ").Trim() == Space().Replace(actual, " ").Trim();

    private static bool SameLines(string expected, string? actual) =>
        actual is not null && Lines(expected) == Lines(actual);

    private static string Lines(string text) => Blank().Replace(text.Replace("\r", string.Empty).Trim(), "\n\n");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"\n[ \t]*\n[ \t\n]*")]
    private static partial Regex Blank();
}
