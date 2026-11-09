using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One item line of a format 2 queue.</summary>
/// <param name="Section">The section letter it sits under.</param>
/// <param name="Person">The person, empty when the line names only the company.</param>
/// <param name="Company">The company, empty when the line names only the person.</param>
/// <param name="Reference">The Count or thread the item answers.</param>
/// <param name="Draft">The draft's path from the share's root, empty when the item is held.</param>
/// <param name="Held">Why no draft goes out, empty when there is a draft.</param>
public sealed record QueueLine(string Section, string Person, string Company, string Reference, string Draft, string Held);

/// <summary>
/// Format 2 of the 4:00 AM queue, the one the reply lane writes and this program reads (NIGHTLY-LANE.md, "The queue
/// format"; check_reply_queue.py reads the same grammar on the share). The file opens with the marker line, and under
/// sections A, B, C and E every line is an item or a note:
/// <c>- **Person** | Company | Count 3406 | draft: nightly/.../drafts/li-followup-clayco.txt</c>, or the same with
/// <c>held: why</c> in place of the draft, a <c>-</c> for an empty person or company, and <c>Note: ...</c> for prose.
/// Section D is free text. Any other line under A, B, C or E is a line this program could not read, and is named.
/// </summary>
public static partial class ReplyQueueTwo
{
    /// <summary>The marker on the first line that holds text.</summary>
    public const string Marker = "<!-- reply-queue: 2 -->";

    /// <summary>The sections whose every line is an item or a note.</summary>
    public static readonly IReadOnlyList<string> Strict = ["A", "B", "C", "E"];

    /// <summary>Whether a queue is in format 2.</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static bool Is(string queue) => queue.Replace("\r", string.Empty).Split('\n').Any(line => line.Trim() == Marker);

    /// <summary>Every item line under the strict sections, in file order.</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static IReadOnlyList<QueueLine> Lines(string queue)
    {
        var lines = new List<QueueLine>();
        foreach ((string section, string line) in Body(queue))
        {
            if (Item().Match(line) is { Success: true } m)
            {
                bool draft = m.Groups["kind"].Value == "draft";
                string value = m.Groups["value"].Value.Trim();
                lines.Add(new QueueLine(section, Field(m.Groups["person"].Value), Field(m.Groups["company"].Value), Field(m.Groups["ref"].Value), draft ? value.Replace('\\', '/') : string.Empty, draft ? string.Empty : value));
            }
        }
        return lines;
    }

    /// <summary>Each line under a strict section that is neither an item nor a note, as "section: line".</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static IReadOnlyList<string> Unread(string queue) =>
        [.. Body(queue).Where(entry => !Item().IsMatch(entry.Line) && !entry.Line.StartsWith("Note: ", StringComparison.Ordinal)).Select(entry => $"{entry.Section}: {entry.Line}")];

    private static string Field(string value) => value.Trim() is "-" or "" ? string.Empty : value.Trim();

    private static IEnumerable<(string Section, string Line)> Body(string queue)
    {
        string? section = null;
        foreach (string raw in queue.Replace("\r", string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd();
            if (Heading().Match(line) is { Success: true } heading)
            {
                section = heading.Groups[1].Value;
                continue;
            }
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                section = null;
                continue;
            }
            if (section is not null && Strict.Contains(section) && line.Trim().Length > 0)
            {
                yield return (section, line.Trim());
            }
        }
    }

    [GeneratedRegex(@"^## ([A-Z])\. ")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^- \*\*(?<person>[^*|]+)\*\* \| (?<company>[^|]+) \| (?<ref>[^|]+) \| (?<kind>draft|held): (?<value>\S.*)$")]
    private static partial Regex Item();
}
