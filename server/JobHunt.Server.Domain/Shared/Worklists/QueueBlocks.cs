using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One drafted block out of a linkedin-proof queue.</summary>
/// <param name="Number">The block's number in its file.</param>
/// <param name="Person">The person, empty when the text carries [First name].</param>
/// <param name="Company">The company.</param>
/// <param name="Action">The action its heading names.</param>
/// <param name="Text">The fenced text, as drafted.</param>
/// <param name="Source">file:line of the heading.</param>
/// <param name="Hot">The block's "- Hot:" bucket, A to E, and E when it has none.</param>
/// <param name="Url">The block's "- Profile:" address, empty when it has none.</param>
public sealed record QueueBlock(int Number, string Person, string Company, WorkAction Action, string Text, string Source, string Hot = "E", string Url = "");

/// <summary>
/// Reads the invite and message blocks of linkedin-proof/QUEUE-*.md, with the bucket and the profile address the feed
/// writes under each heading, and leaves out every block the file or its status
/// file says is finished. A block is open only when nothing says otherwise and its status, if it has one, says NOT SENT;
/// anything held, dropped, replaced, pending or sent stays out, because a block wrongly left out waits a day and a block
/// wrongly let in is a second touch.
/// </summary>
public static partial class QueueBlocks
{
    /// <summary>The open blocks of one queue.</summary>
    /// <param name="name">The queue's file name.</param>
    /// <param name="text">The queue's text.</param>
    /// <param name="statuses">The text of each status file naming this queue.</param>
    public static IReadOnlyList<QueueBlock> Open(string name, string text, IEnumerable<string> statuses)
    {
        string[] lines = text.Split('\n');
        List<QueueBlock> blocks = Parse(name, lines);
        var closed = new HashSet<int>();
        foreach (string line in lines)
        {
            if (Replaces().Match(line) is { Success: true } replaced)
            {
                closed.Add(int.Parse(replaced.Groups[1].Value));
            }
            if (AlreadySent().Match(line) is { Success: true } sent)
            {
                closed.Add(int.Parse(sent.Groups[1].Value));
            }
            if (ItemClosed().Match(line) is { Success: true } item)
            {
                closed.Add(int.Parse(item.Groups[1].Value));
            }
        }
        var texts = new Dictionary<int, (string Person, string Company, string Text)>();
        foreach (string status in statuses)
        {
            ReadStatus(status, closed, texts);
        }
        return [.. blocks
            .Select(block => texts.TryGetValue(block.Number, out var filled) ? block with { Person = filled.Person, Company = filled.Company, Text = filled.Text } : block)
            .Where(block => !closed.Contains(block.Number))];
    }

    /// <summary>The queue a status file reports on, or null when it names none.</summary>
    /// <param name="status">The status file's text.</param>
    public static string? StatusOf(string status) => Status().Match(status) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>The names on a queue's agency-submitted line.</summary>
    /// <param name="text">A queue's text.</param>
    public static IReadOnlyList<string> Agencies(string text)
    {
        Match line = AgencyLine().Match(text);
        return line.Success
            ? [.. line.Groups[1].Value.Split(',').Select(name => name.Replace("for Liventus", "").Trim(' ', '.', '\r')).Where(name => name.Length > 0)]
            : [];
    }

    private static List<QueueBlock> Parse(string name, string[] lines)
    {
        var blocks = new List<QueueBlock>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match heading = Heading().Match(lines[i]);
            if (!heading.Success)
            {
                continue;
            }
            int open = Array.FindIndex(lines, i + 1, line => line.TrimEnd().StartsWith("```", StringComparison.Ordinal) || line.StartsWith('#'));
            if (open < 0 || lines[open].StartsWith('#'))
            {
                continue;
            }
            int close = Array.FindIndex(lines, open + 1, line => line.TrimEnd().StartsWith("```", StringComparison.Ordinal));
            if (close < 0)
            {
                continue;
            }
            string title = heading.Groups[2].Value;
            string who = Who().Match(title).Groups[1].Value;
            string[] parts = [.. who.Split(',').Select(part => part.Trim())];
            bool named = parts.Length > 1;
            string text = string.Join('\n', lines[(open + 1)..close].Select(line => line.TrimEnd('\r'))).Trim();
            string[] meta = lines[(i + 1)..open];
            string hot = meta.Select(line => HotLine().Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value.ToUpperInvariant() ?? "E";
            string url = meta.Select(line => ProfileLine().Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value ?? string.Empty;
            blocks.Add(new QueueBlock(
                int.Parse(heading.Groups[1].Value),
                named ? parts[0] : string.Empty,
                FirstWords(parts[^1]),
                title.Contains("MESSAGE", StringComparison.Ordinal) ? WorkAction.Message : WorkAction.Invite,
                text,
                $"{name}:{i + 1}",
                hot,
                url));
        }
        return blocks;
    }

    private static void ReadStatus(string status, HashSet<int> closed, Dictionary<int, (string Person, string Company, string Text)> texts)
    {
        foreach (Match row in StatusRow().Matches(status))
        {
            string state = row.Groups["state"].Value;
            bool open = (state.StartsWith("NOT SENT", StringComparison.Ordinal) || state.StartsWith("DRAFTED, NOT SENT", StringComparison.Ordinal))
                && !Pending().IsMatch(state);
            foreach (Match number in Number().Matches(row.Groups["blocks"].Value))
            {
                if (!open)
                {
                    closed.Add(int.Parse(number.Value));
                }
            }
        }
        foreach (Match block in StatusText().Matches(status))
        {
            texts[int.Parse(block.Groups["number"].Value)] = (block.Groups["person"].Value.Trim(), FirstWords(block.Groups["company"].Value), block.Groups["text"].Value.Replace("\r", string.Empty).Trim());
        }
    }

    private static string FirstWords(string company) => Trailing().Replace(company, string.Empty).Trim();

    [GeneratedRegex(@"^###\s+(\d+)\.\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^- Hot:\s*([A-Ea-e])\b")]
    private static partial Regex HotLine();

    [GeneratedRegex(@"^- Profile:\s*(https://www\.linkedin\.com/in/[\w%-]+/?)\s*$")]
    private static partial Regex ProfileLine();

    [GeneratedRegex(@"^([^:(]+?)(?:\s+\(|:|$)")]
    private static partial Regex Who();

    [GeneratedRegex(@"\(replaces item (\d+)")]
    private static partial Regex Replaces();

    [GeneratedRegex(@"^- Block (\d+) .*DO NOT SEND AGAIN")]
    private static partial Regex AlreadySent();

    [GeneratedRegex(@"^- Item (\d+) .*(DROPPED|[Ss]end nothing)")]
    private static partial Regex ItemClosed();

    [GeneratedRegex(@"Status of (QUEUE-[\w-]+\.md)")]
    private static partial Regex Status();

    [GeneratedRegex(@"(?m)^\|\s*(?<blocks>\d+(?:\s*/\s*\d+)*)\s*\|[^|]*\|\s*(?<state>[^|]*)\|")]
    private static partial Regex StatusRow();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    [GeneratedRegex(@"(?<!not )\bpending\b", RegexOptions.IgnoreCase)]
    private static partial Regex Pending();

    [GeneratedRegex(@"(?ms)^## Block (?<number>\d+) text[^(\n]*\((?<person>[^,]+),\s*(?<company>[^,]+),[^)]*\)\s*^```\r?\n(?<text>.*?)^```")]
    private static partial Regex StatusText();

    [GeneratedRegex(@"(?m)^- Agency or recruiter-submitted:\s*([^\r\n]*?)(?:\s+Sogeti:.*)?$")]
    private static partial Regex AgencyLine();

    [GeneratedRegex(@"\s+(AI Platform|Foundry|\(.*\))$")]
    private static partial Regex Trailing();
}
