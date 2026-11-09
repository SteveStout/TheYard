using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// The card's "Rejection reads" (PLAN-AUTOHUNT rung 2): the reply lane appends each read under that heading at the end
/// of TODAY.md, so the morning card, which rewrites the file whole, carries the section forward and adds any read in
/// the newest queue's section E that it does not hold yet. A read he has answered, or whose draft has a DO-NOT-SEND
/// note beside it, is dropped and never comes back. The dispatcher's held-item lines, which land at the end of the
/// file too, are left out: the lanes section reads STATUS.md instead.
/// </summary>
public static partial class TodayReads
{
    /// <summary>The heading the reply lane appends under.</summary>
    public const string Heading = "## Rejection reads";

    /// <summary>The blocks under the last Rejection reads heading, up to the next heading, the dispatcher's lines left out.</summary>
    /// <param name="previous">TODAY.md before this card.</param>
    public static IReadOnlyList<string> Carried(string previous)
    {
        var lines = TodayParts.Lines(previous).ToList();
        int at = lines.FindLastIndex(l => l.Trim() == Heading);
        if (at < 0)
        {
            return [];
        }
        var under = lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).Where(l => !Dispatcher().IsMatch(l));
        return TodayParts.Blocks(under);
    }

    /// <summary>The draft a read names after "Draft: ", as a path from the share's root, or empty.</summary>
    /// <param name="block">The read.</param>
    public static string DraftOf(string block)
    {
        var m = Draft().Match(block);
        return m.Success ? ShareRoot(m.Groups[1].Value) : string.Empty;
    }

    /// <summary>A path as the share's root names it: forward slashes, the drive or mount and any trailing stop taken off.</summary>
    /// <param name="path">The path as a lane wrote it.</param>
    public static string ShareRoot(string path) => Root().Replace(path.Replace('\\', '/').TrimEnd('.', ',', ';', ')'), string.Empty);

    /// <summary>The carried reads he has not answered, then each new section E read with its ask in full.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="rejections">Section E's items still shown.</param>
    /// <param name="answered">The answered phrases.</param>
    public static IReadOnlyList<string> Rebuild(TodayInputs inputs, IReadOnlyList<TodayItem> rejections, IReadOnlyList<string> answered)
    {
        var reads = Carried(inputs.Previous)
            .Where(b => !TodayParts.IsAnswered(b, answered) && !inputs.DoNotSend.Contains(DraftOf(b)))
            .ToList();
        foreach (var item in rejections)
        {
            string read = item.Notes.Where(n => n.StartsWith("read: ", StringComparison.Ordinal)).Select(n => n["read: ".Length..].Trim()).FirstOrDefault()
                ?? $"{item.Who}, a rejection from a live room (the queue holds no read line)";
            if (reads.Any(r => r.Contains(read, StringComparison.Ordinal)))
            {
                continue;
            }
            var block = new StringBuilder($"- {read}");
            if (item.Draft.Length > 0 && inputs.Drafts.TryGetValue(item.Draft, out string? ask))
            {
                block.Append(" Draft: ").Append(item.Draft).Append('\n');
                block.Append("  CRITICAL, an email under your name to ").Append(item.Person.Length > 0 ? item.Person : "-").Append(" at ").Append(item.Company.Length > 0 ? item.Company : "-").Append(". Feedback ask, full text:\n");
                foreach (string line in TodayParts.Lines(ask.Trim()))
                {
                    block.Append("   > ").Append(line).Append('\n');
                }
                block.Append("  Say \"send\" to send it, or let it go.");
            }
            else
            {
                block.Append(" Held: ").Append(item.Held.Length > 0 ? item.Held : "no draft file");
            }
            reads.Add(block.ToString());
        }
        return reads;
    }

    [GeneratedRegex(@"^- \w{3} \d{2}/\d{2} \d{2}:\d{2} C[SD]T, dispatch: ")]
    private static partial Regex Dispatcher();

    [GeneratedRegex(@"Draft: (\S+)")]
    private static partial Regex Draft();

    [GeneratedRegex(@"^(?:[A-Za-z]:/|/mnt/j/|\$JOBHUNT/|~/J/)")]
    private static partial Regex Root();
}
