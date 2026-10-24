using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// The one page Steve reads (the automation plan's third increment): what waits on him, what the next
/// seven days need, whether the pipeline is healthy, and whether TheYard is green, written by the
/// Function with no model. Each line names the file or answer it was read from, and a fact that could
/// not be read says so in words, never as a blank or a zero.
/// </summary>
public static partial class DailyCard
{
    /// <summary>The card on the share.</summary>
    public const string Path = "nightly/CARD.md";

    /// <summary>The card's text for the facts read.</summary>
    public static string Render(CardFacts facts)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# CARD, {facts.At:dddd yyyy-MM-dd HH:mm} Central\r\n\r\n");
        text.Append("Written by the job hunt's Function, no model. Every line names where it was read.\r\n\r\n");
        text.Append("## Waiting on you\r\n\r\n");
        if (facts.Blockers.Count == 0 && facts.Held.Count == 0)
        {
            text.Append("- Nothing.\r\n");
        }
        foreach (string blocker in facts.Blockers)
        {
            text.Append("- ").Append(blocker).Append("\r\n");
        }
        foreach (string held in facts.Held)
        {
            text.Append("- ").Append(held).Append("\r\n");
        }
        text.Append("\r\n## The next seven days\r\n\r\n");
        foreach (string line in facts.Week)
        {
            text.Append("- ").Append(line).Append("\r\n");
        }
        text.Append("\r\n## The pipeline\r\n\r\n");
        foreach (string line in facts.Pipeline)
        {
            text.Append("- ").Append(line).Append("\r\n");
        }
        text.Append("\r\n## TheYard\r\n\r\n");
        foreach (string line in facts.Yard)
        {
            text.Append("- ").Append(line).Append("\r\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// The items an APPLY-BLOCKERS file lists for Steve: each numbered line's bold name, with the
    /// sentence after it cut at the first full stop, under the file's own name.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <param name="text">Its text.</param>
    public static IReadOnlyList<string> Blockers(string name, string text) =>
        [.. Numbered().Matches(text).Select(m => $"{m.Groups[1].Value.Trim().TrimEnd('.')}: {First(m.Groups[2].Value)} ({name})")];

    /// <summary>The newest APPLY-BLOCKERS file among names, if it is no older than three days before <paramref name="today"/>.</summary>
    public static string? Newest(IEnumerable<string> names, DateOnly today) => names
        .Select(n => (Name: n, Match: Dated().Match(n)))
        .Where(n => n.Match.Success && DateOnly.TryParseExact(n.Match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && today.DayNumber - d.DayNumber is >= 0 and <= 3)
        .OrderByDescending(n => n.Name, StringComparer.Ordinal)
        .Select(n => n.Name)
        .FirstOrDefault();

    /// <summary>The card without its title line, so two cards that differ only in when they were written read as the same.</summary>
    public static string Body(string card)
    {
        int line = card.IndexOf('\n', StringComparison.Ordinal);
        return line < 0 ? card : card[(line + 1)..];
    }

    private static string First(string sentence)
    {
        string s = sentence.Trim();
        int stop = s.IndexOf(". ", StringComparison.Ordinal);
        return stop < 0 ? s.TrimEnd('.') : s[..stop];
    }

    [GeneratedRegex(@"(?m)^\d+\.\s+\*\*(.+?)\*\*\s*(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^APPLY-BLOCKERS-(\d{4}-\d{2}-\d{2})\.md$")]
    private static partial Regex Dated();
}

/// <summary>What the card says, read before it is drawn.</summary>
/// <param name="At">When, in Central time.</param>
/// <param name="Blockers">Application blockers, one a line.</param>
/// <param name="Held">Records held for him (a Sheet stop, a rejection with a reason).</param>
/// <param name="Week">The next seven days.</param>
/// <param name="Pipeline">The merger, the appends, the Sheet mirror, the ATS sweep, the tracker.</param>
/// <param name="Yard">TheYard's CI and live versions.</param>
public sealed record CardFacts(DateTime At, IReadOnlyList<string> Blockers, IReadOnlyList<string> Held, IReadOnlyList<string> Week, IReadOnlyList<string> Pipeline, IReadOnlyList<string> Yard);
