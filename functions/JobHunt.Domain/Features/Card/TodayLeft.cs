using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>A decision a lane left for him, known by its lettered bold title ("**B. One email after ...**").</summary>
/// <param name="Letter">Its letter.</param>
/// <param name="Title">Its title, the words after the letter up to the first stop, colon or bracket.</param>
/// <param name="Cards">The closed lanes' sections that name it, each one more card that put it in front of him.</param>
/// <param name="Default">What holds when he does not answer ("Default: no."), or null when no section says.</param>
/// <param name="Answered">Whether a section that asks it names a phrase in ANSWERED.md.</param>
public sealed record TodayDecision(string Letter, string Title, int Cards, string? Default, bool Answered)
{
    /// <summary>Asked on three cards with no answer and a default to fall back on, so it is closed as that default.</summary>
    public bool Closed => !Answered && Default is not null && Cards >= TodayLeft.Cards;
}

/// <summary>
/// What closed lanes left for him, one line per decision: a later lane often asks the same decision again or notes it
/// closed, and on 11/12 the card printed both. A decision is a block that opens with a lettered bold title; every
/// block in any section that names the title is the same decision. The card prints it once, under the first section
/// of the day that names it, and once three cards have asked it with no answer it is closed as its default and prints
/// as one line instead. A lead-in line ("so that line again:") goes when nothing it led into is left.
/// </summary>
public static partial class TodayLeft
{
    /// <summary>The cards a decision is asked on before it closes as its default.</summary>
    public const int Cards = 3;

    /// <summary>Every lettered decision in the sections, with the cards that named it, its default and whether he answered it.</summary>
    /// <param name="sections">Every closed lane's section up to the card's time.</param>
    /// <param name="answered">The answered phrases.</param>
    public static IReadOnlyList<TodayDecision> Decisions(IReadOnlyList<LeftSection> sections, IReadOnlyList<string> answered)
    {
        var keys = sections.SelectMany(s => s.Blocks).Select(b => Key().Match(b)).Where(m => m.Success)
            .Select(m => (Letter: m.Groups["letter"].Value, Title: m.Groups["title"].Value.Trim()))
            .DistinctBy(k => k.Title.ToUpperInvariant()).ToList();
        var decisions = new List<TodayDecision>();
        foreach (var (letter, title) in keys)
        {
            int cards = sections.Count(s => s.Blocks.Any(b => Names(b, title)));
            // Its own blocks, the ones that open with its title, say its default and carry his answer; a block that
            // names several decisions in passing answers none of them.
            var own = sections.SelectMany(s => s.Blocks).Where(b => Key().Match(b) is { Success: true } m && string.Equals(m.Groups["title"].Value.Trim(), title, StringComparison.OrdinalIgnoreCase)).ToList();
            string? fallback = own.Select(b => DefaultOf().Match(b)).Where(m => m.Success).Select(m => m.Groups["d"].Value.Trim()).LastOrDefault();
            decisions.Add(new TodayDecision(letter, title, cards, fallback, own.Any(b => TodayParts.IsAnswered(b, answered))));
        }
        return decisions;
    }

    /// <summary>
    /// The day's sections with each decision kept once, under the first section that names it, a closed decision's
    /// blocks taken out and a lead-in kept only before what it leads into; then the closed decisions those sections named.
    /// </summary>
    /// <param name="shown">The sections of the day, each with the blocks the card would print.</param>
    /// <param name="decisions">Every decision (<see cref="Decisions"/>).</param>
    public static (IReadOnlyList<(LeftSection Section, IReadOnlyList<string> Blocks)> Sections, IReadOnlyList<TodayDecision> Closed) Shown(
        IReadOnlyList<(LeftSection Section, IReadOnlyList<string> Blocks)> shown, IReadOnlyList<TodayDecision> decisions)
    {
        var printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closed = new List<TodayDecision>();
        var sections = new List<(LeftSection, IReadOnlyList<string>)>();
        foreach (var (section, blocks) in shown)
        {
            var kept = new List<string>();
            foreach (string block in blocks)
            {
                var named = decisions.Where(d => Names(block, d.Title)).ToList();
                closed.AddRange([.. named.Where(d => d.Closed && !closed.Contains(d))]);
                if (named.Any(d => d.Closed || printed.Contains(d.Title)))
                {
                    continue;
                }
                printed.UnionWith(named.Select(d => d.Title));
                kept.Add(block);
            }
            var led = kept.Where((b, i) => !LeadIn(b) || (i + 1 < kept.Count && !LeadIn(kept[i + 1]))).ToList();
            if (led.Count > 0)
            {
                sections.Add((section, led));
            }
        }
        return (sections, closed);
    }

    /// <summary>
    /// The proposed items a lane asks his yes on: each file name an unanswered LEFT FOR STEVE bullet names, in any
    /// section up to the card, with the newest lane that names it. A bullet of a closed decision asks nothing.
    /// </summary>
    /// <param name="sections">Every closed lane's section up to the card's time.</param>
    /// <param name="decisions">Every decision (<see cref="Decisions"/>).</param>
    /// <param name="answered">The answered phrases.</param>
    /// <param name="proposed">The proposed file names in STATUS.md.</param>
    public static IReadOnlyDictionary<string, string> Asks(IReadOnlyList<LeftSection> sections, IReadOnlyList<TodayDecision> decisions, IReadOnlyList<string> answered, IReadOnlyList<string> proposed)
    {
        var asks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in sections.OrderBy(s => s.Closed))
        {
            foreach (string block in section.Blocks.Where(b => !TodayParts.IsAnswered(b, answered) && !decisions.Any(d => d.Closed && Names(b, d.Title))))
            {
                foreach (string name in proposed.Where(p => block.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    asks[name] = section.Lane;
                }
            }
        }
        return asks;
    }

    /// <summary>Whether a block names the decision's title, case aside.</summary>
    /// <param name="block">The block.</param>
    /// <param name="title">The title.</param>
    public static bool Names(string block, string title) => block.Contains(title, StringComparison.OrdinalIgnoreCase);

    // A line of prose that ends in a colon and leads into the bullets after it.
    private static bool LeadIn(string block) => !block.StartsWith("- ", StringComparison.Ordinal) && !block.Contains('\n', StringComparison.Ordinal) && block.TrimEnd().EndsWith(':');

    [GeneratedRegex(@"^(?:- |\d+\. )?\*\*(?<letter>[A-Z])\. (?<title>[^*.:(\n]{12,}?)\s*(?=[.:(]|\*\*)")]
    private static partial Regex Key();

    [GeneratedRegex(@"Default(?: if unanswered)?: (?<d>[^.,;\n]+)")]
    private static partial Regex DefaultOf();
}
