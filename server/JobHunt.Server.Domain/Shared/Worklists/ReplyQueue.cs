using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One candidate out of the 4:00 AM queue's B or C section.</summary>
/// <param name="Person">The person, empty for a Rule 58 window that names only the company.</param>
/// <param name="Company">The company, empty for an accept that names only the person.</param>
/// <param name="Source">The section and Count it came from.</param>
public sealed record ReplyItem(string Person, string Company, string Source);

/// <summary>
/// Reads section B (accepts) and section C (Rule 58 windows) of the newest linkedin-queue.md. The queue names people
/// and companies and points at a folder of drafts; it holds no text of its own, so a row gets its text from a draft
/// file or is dropped. Section A is inbound mail for a person to read, and no worklist is built from it.
/// </summary>
public static partial class ReplyQueue
{
    /// <summary>The candidates of both sections.</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static IReadOnlyList<ReplyItem> Items(string queue)
    {
        var items = new List<ReplyItem>();
        items.AddRange(Accept().Matches(Section(queue, "B")).Select(m => new ReplyItem(m.Groups[1].Value.Trim(), string.Empty, "linkedin-queue.md B")));
        foreach (Match day in Window().Matches(Section(queue, "C")))
        {
            foreach (string part in day.Groups[1].Value.TrimEnd('.', ' ', '\r').Split(','))
            {
                string entry = Note().Replace(part, string.Empty).Trim();
                Match company = CompanyCount().Match(entry);
                if (company.Success)
                {
                    items.Add(new ReplyItem(string.Empty, company.Groups[1].Value.Trim(), $"linkedin-queue.md C, Count {company.Groups[2].Value}"));
                }
            }
        }
        return items;
    }

    /// <summary>The folder section C says holds its drafts, relative to the share, or null.</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static string? DraftFolder(string queue) =>
        Drafts().Match(queue) is { Success: true } match ? match.Groups[1].Value.Replace('\\', '/') : null;

    /// <summary>The draft's file name for a company.</summary>
    /// <param name="company">The company.</param>
    public static string DraftName(string company) => $"li-followup-{NotLetter().Replace(company.ToLowerInvariant().Split(' ')[0], string.Empty)}.txt";

    /// <summary>A draft's body: the file with its OBJECTIVE and FILES READ lines cut.</summary>
    /// <param name="draft">The draft file's text.</param>
    public static string Body(string draft) =>
        string.Join('\n', draft.Replace("\r", string.Empty).Split('\n').Where(line => !Label().IsMatch(line))).Trim();

    /// <summary>The first name a draft greets, or an empty string.</summary>
    /// <param name="body">The draft's body.</param>
    public static string Greeted(string body) => Greeting().Match(body) is { Success: true } match ? match.Groups[1].Value : string.Empty;

    private static string Section(string queue, string letter)
    {
        int at = queue.IndexOf($"## {letter}. ", StringComparison.Ordinal);
        if (at < 0)
        {
            return string.Empty;
        }
        int end = queue.IndexOf("\n## ", at + 3, StringComparison.Ordinal);
        return end < 0 ? queue[at..] : queue[at..end];
    }

    [GeneratedRegex(@"(?m)^- \*\*([^*]+)\*\*")]
    private static partial Regex Accept();

    [GeneratedRegex(@"(?m)^- Day \d+ \([^)]*\):\s*(.+)$")]
    private static partial Regex Window();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Note();

    [GeneratedRegex(@"^(.+?)\s+(\d{3,5})(?:\s+and\s+\d{3,5})?$")]
    private static partial Regex CompanyCount();

    [GeneratedRegex(@"(?m)^- Drafts:\s*(\S+?)[\\/]li-followup-")]
    private static partial Regex Drafts();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NotLetter();

    [GeneratedRegex(@"^(OBJECTIVE|FILES READ|NO-RESUME|To|Subject):")]
    private static partial Regex Label();

    [GeneratedRegex(@"^([A-Z][\w'-]+),\s*$", RegexOptions.Multiline)]
    private static partial Regex Greeting();
}
