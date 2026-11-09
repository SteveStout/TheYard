using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>One item of the reply queue's format 2, with the <c>Note: </c> lines that follow it.</summary>
/// <param name="Section">The section letter it sits under.</param>
/// <param name="Person">The person, empty when the line names only the company.</param>
/// <param name="Company">The company, empty when the line names only the person.</param>
/// <param name="Reference">The Count or thread the item answers.</param>
/// <param name="Draft">The draft's path from the share's root, empty when the item is held.</param>
/// <param name="Held">Why no draft goes out, empty when there is a draft.</param>
/// <param name="Notes">The notes under it, each without its <c>Note: </c>.</param>
public sealed record TodayItem(string Section, string Person, string Company, string Reference, string Draft, string Held, IReadOnlyList<string> Notes)
{
    /// <summary>Whether the item answers an email thread, so its send goes out under his name from Gmail.</summary>
    public bool IsEmail => Reference.StartsWith("thread: gmail", StringComparison.Ordinal);

    /// <summary>The person, else the company, else a dash.</summary>
    public string Who => Person.Length > 0 ? Company.Length > 0 ? $"{Person} ({Company})" : Person : Company.Length > 0 ? Company : "-";
}

/// <summary>One closed lane's section of the dispatcher's LEFT-FOR-STEVE.md.</summary>
/// <param name="Lane">The lane.</param>
/// <param name="Closed">When it closed, in Central time.</param>
/// <param name="Blocks">Its LEFT FOR STEVE items, each a bullet with the lines that continue it.</param>
public sealed record LeftSection(string Lane, DateTime Closed, IReadOnlyList<string> Blocks);

/// <summary>
/// The readers behind the morning card: the reply queue's format 2 (NIGHTLY-LANE.md, "The queue format"; the 6 AM
/// program's ReplyQueueTwo reads the same grammar), the dispatcher's STATUS.md and LEFT-FOR-STEVE.md, the answered
/// list, and the one-bullet files (walls, the decision). Every reader takes text and answers text; nothing here
/// reads the share.
/// </summary>
public static partial class TodayParts
{
    /// <summary>The marker on the first line of a format 2 queue.</summary>
    public const string Marker = "<!-- reply-queue: 2 -->";

    /// <summary>Every item under sections A, B, C and E, in file order, each with the notes that follow it.</summary>
    /// <param name="queue">linkedin-queue.md.</param>
    public static IReadOnlyList<TodayItem> Items(string queue)
    {
        var items = new List<TodayItem>();
        List<string>? notes = null;
        string? section = null;
        foreach (string raw in Lines(queue))
        {
            string line = raw.Trim();
            if (SectionHeading().Match(raw) is { Success: true } heading)
            {
                section = heading.Groups[1].Value;
                notes = null;
                continue;
            }
            if (raw.StartsWith('#'))
            {
                section = null;
                continue;
            }
            if (section is not ("A" or "B" or "C" or "E") || line.Length == 0)
            {
                continue;
            }
            if (Item().Match(line) is { Success: true } m)
            {
                bool draft = m.Groups["kind"].Value == "draft";
                string value = m.Groups["value"].Value.Trim();
                notes = [];
                items.Add(new TodayItem(section, Field(m.Groups["person"].Value), Field(m.Groups["company"].Value), Field(m.Groups["ref"].Value), draft ? value.Replace('\\', '/') : string.Empty, draft ? string.Empty : value, notes));
            }
            else if (line.StartsWith("Note: ", StringComparison.Ordinal) && notes is not null)
            {
                notes.Add(line["Note: ".Length..].Trim());
            }
        }
        return items;
    }

    /// <summary>The newest real reply run's folder among a day's folders that hold a queue, never a dry one.</summary>
    /// <param name="folders">Each folder of the day with whether it holds linkedin-queue.md.</param>
    public static string? NewestRun(IEnumerable<(string Folder, bool HasQueue)> folders) => folders
        .Where(f => f.HasQueue && ReplyRetry.IsRun(f.Folder))
        .Select(f => f.Folder)
        .OrderByDescending(f => f, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// The lanes from STATUS.md: what runs, a pause, every held item with its reason, and every proposed item as a
    /// "queue it?" line. The ready queue is the dispatcher's own business and is left off.
    /// </summary>
    /// <param name="status">STATUS.md.</param>
    public static IReadOnlyList<string> Lanes(string status)
    {
        var lines = new List<string>();
        string? section = null;
        foreach (string raw in Lines(status))
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                section = raw[3..].Trim();
                continue;
            }
            if (!raw.StartsWith("- ", StringComparison.Ordinal) || section is null)
            {
                continue;
            }
            string item = raw[2..].Trim();
            if (section == "Running")
            {
                lines.Add("Running: " + item);
            }
            else if (section == "Paused" && item != "no")
            {
                lines.Add("Paused: " + item);
            }
            else if (section == "Held")
            {
                lines.Add("Held: " + item);
            }
            else if (section.StartsWith("Proposed", StringComparison.Ordinal))
            {
                lines.Add($"Proposed: {item}. Queue it?");
            }
        }
        return lines;
    }

    /// <summary>The time line STATUS.md opens with ("Written Mon 11/09 12:46:29 CST by dispatch.py."), or null.</summary>
    /// <param name="status">STATUS.md.</param>
    public static string? Written(string status) => Lines(status).FirstOrDefault(l => l.StartsWith("Written ", StringComparison.Ordinal))?.Trim().TrimEnd('.');

    /// <summary>Every closed lane's section of LEFT-FOR-STEVE.md, its closing read in the card's year.</summary>
    /// <param name="text">LEFT-FOR-STEVE.md.</param>
    /// <param name="at">The card's time, for the year the file leaves out.</param>
    public static IReadOnlyList<LeftSection> Left(string text, DateTime at)
    {
        var sections = new List<LeftSection>();
        string? lane = null;
        DateTime closed = default;
        var body = new List<string>();
        void Close()
        {
            if (lane is not null)
            {
                sections.Add(new LeftSection(lane, closed, Blocks(body)));
            }
            body.Clear();
        }
        foreach (string raw in Lines(text))
        {
            if (Closed().Match(raw) is { Success: true } m)
            {
                Close();
                lane = m.Groups["lane"].Value.Trim();
                closed = When(m, at);
                continue;
            }
            if (raw.Trim() != "## LEFT FOR STEVE")
            {
                body.Add(raw);
            }
        }
        Close();
        return sections;
    }

    /// <summary>
    /// Text cut into blocks: each bullet at the left edge with the indented or blank lines that continue it, and each
    /// other line at the left edge on its own. Empty items ("none", "nothing"), FOLLOW-ON lines and registry
    /// read-backs are dropped, because none of them asks anything of him.
    /// </summary>
    /// <param name="lines">The lines.</param>
    public static IReadOnlyList<string> Blocks(IEnumerable<string> lines)
    {
        var blocks = new List<List<string>>();
        foreach (string raw in lines)
        {
            bool continues = raw.Length == 0 || char.IsWhiteSpace(raw[0]);
            if (continues && blocks.Count > 0)
            {
                blocks[^1].Add(raw);
            }
            else if (!continues)
            {
                blocks.Add([raw]);
            }
        }
        return [.. blocks.Select(b => string.Join("\n", b).TrimEnd()).Where(Asks)];
    }

    /// <summary>The phrases Steve has answered: each bullet's text before its first " | ".</summary>
    /// <param name="answered">ANSWERED.md.</param>
    public static IReadOnlyList<string> Answered(string answered) =>
        [.. Bullets(answered).Select(b => b.Split(" | ")[0].Trim()).Where(p => p.Length >= 4)];

    /// <summary>Whether a text names any answered phrase, case aside.</summary>
    /// <param name="text">The text.</param>
    /// <param name="answered">The answered phrases.</param>
    public static bool IsAnswered(string text, IReadOnlyList<string> answered) =>
        answered.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Each bullet's text at the left edge of a file, "none" and "nothing" left out.</summary>
    /// <param name="text">The file.</param>
    public static IReadOnlyList<string> Bullets(string text) =>
        [.. Lines(text).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..].Trim()).Where(l => !Empty().IsMatch(l))];

    /// <summary>The file's lines, its line ends read either way.</summary>
    /// <param name="text">The text.</param>
    public static IEnumerable<string> Lines(string text) => text.Replace("\r", string.Empty).Split('\n').Select(l => l.TrimEnd());

    private static bool Asks(string block)
    {
        string first = block.Split('\n')[0].Trim();
        string item = first.StartsWith("- ", StringComparison.Ordinal) ? first[2..].Trim() : first;
        return item.Length > 0 && !Empty().IsMatch(item) && !item.StartsWith("FOLLOW-ON:", StringComparison.Ordinal) && !item.StartsWith("CLOSED row", StringComparison.Ordinal) && !item.StartsWith('>');
    }

    private static DateTime When(Match m, DateTime at)
    {
        int month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        var when = new DateTime(at.Year, month, day, int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture), 0);
        return when > at.AddDays(1) ? when.AddYears(-1) : when;
    }

    private static string Field(string value) => value.Trim() is "-" or "" ? string.Empty : value.Trim();

    [GeneratedRegex(@"^## ([A-Z])\. ")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^- \*\*(?<person>[^*|]+)\*\* \| (?<company>[^|]+) \| (?<ref>[^|]+) \| (?<kind>draft|held): (?<value>\S.*)$")]
    private static partial Regex Item();

    [GeneratedRegex(@"^## (?<lane>.+?), closed \w{3} (?<m>\d{2})/(?<d>\d{2}) (?<h>\d{2}):(?<min>\d{2}) C[SD]T")]
    private static partial Regex Closed();

    [GeneratedRegex(@"^(none|nothing)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex Empty();
}
