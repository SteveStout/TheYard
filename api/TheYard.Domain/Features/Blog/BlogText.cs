// How the blog cuts text into words, the one cut the search, the keywords and the reading minutes share. A word is
// a run of letters, digits and #, lower case, so a typed word matches the start of a word (copil finds Copilot)
// and never the middle of one (ms is not in "items"). A dot that opens a word at a word's edge is kept as a second
// word beside it, so ".NET" is found as .net and as net, as the mockup's pattern finds it. Two neighbouring words
// with one space between are kept as a pair, so a phrase such as "microsoft copilot" is one lookup.
namespace TheYard.Domain.Features.Blog;

/// <summary>Cuts text into the words the search and the keyword rules count.</summary>
public static class BlogText
{
    /// <summary>Where a word sits in a post: the title weighs most, then the summary, then the body by its mentions.</summary>
    public const byte Title = 1, Summary = 2, Body = 3;

    /// <summary>The words a reading minute holds (ADR: Every LinkedIn post is also a blog post, the addendum on the read model).</summary>
    public const int WordsPerMinute = 230;

    /// <summary>The longest word kept, the width of blog.PostWord's column; a longer run is no word anybody types.</summary>
    public const int LongestWord = 80;

    private static bool IsWordChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '#';

    /// <summary>
    /// The words of a text in order, lower case: each run of letters, digits and #, and before it, when a dot opens
    /// the run at a word's edge, the run with its dot (".net" and then "net"). Each word comes with whether only one
    /// space stands between it and the word before, which is what makes the two a pair.
    /// </summary>
    /// <param name="text">Any text: a title, a summary, a body's plain text, a label, a typed query.</param>
    public static IReadOnlyList<(string Word, bool Dotted, bool AfterOneSpace)> Words(string text)
    {
        string lower = text.ToLowerInvariant();
        var words = new List<(string, bool, bool)>();
        int i = 0, lastEnd = -1;
        while (i < lower.Length)
        {
            if (!IsWordChar(lower[i]))
            {
                i++;
                continue;
            }
            int start = i;
            while (i < lower.Length && IsWordChar(lower[i]))
            {
                i++;
            }
            string word = lower[start..i];
            bool dotted = start > 0 && lower[start - 1] == '.' && (start == 1 || !IsWordChar(lower[start - 2]));
            if (dotted && word.Length < LongestWord)
            {
                words.Add(("." + word, true, false));
            }
            bool oneSpace = lastEnd >= 0 && start - lastEnd == 1 && lower[lastEnd] == ' ';
            if (word.Length <= LongestWord)
            {
                words.Add((word, false, oneSpace));
            }
            lastEnd = i;
        }
        return words;
    }

    /// <summary>
    /// The search's words for one field: every word with its count, and every pair of neighbours with one space
    /// between, with theirs. A dotted word is counted alone and never paired.
    /// </summary>
    /// <param name="text">The field's text.</param>
    /// <param name="field">Title, Summary or Body.</param>
    /// <param name="keepPair">Which pairs to keep; every pair when null. A store keeps only the pairs a phrase can land on.</param>
    public static IEnumerable<(string Word, byte Field, int Mentions)> Counted(string text, byte field, Func<string, bool>? keepPair = null)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var (word, dotted, afterOneSpace) in Words(text))
        {
            counts[word] = counts.GetValueOrDefault(word) + 1;
            if (dotted)
            {
                continue;
            }
            if (afterOneSpace && previous is not null && previous.Length + word.Length + 1 <= LongestWord)
            {
                string pair = previous + " " + word;
                if (keepPair is null || keepPair(pair))
                {
                    counts[pair] = counts.GetValueOrDefault(pair) + 1;
                }
            }
            previous = word;
        }
        return counts.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, field, entry.Value));
    }

    /// <summary>How many times a term lands in a field's counted words: a term with no space as the start of a word, a phrase as the start of a pair.</summary>
    /// <param name="counted">The field's words and counts.</param>
    /// <param name="term">A widened term, lower case.</param>
    public static int Mentions(IEnumerable<(string Word, int Mentions)> counted, string term) =>
        counted.Where(entry => entry.Word.StartsWith(term, StringComparison.Ordinal) && entry.Word.Contains(' ') == term.Contains(' '))
            .Sum(entry => entry.Mentions);

    /// <summary>The minutes a post takes to read: its body's words over 230, rounded up, and at least one.</summary>
    /// <param name="plainBody">The body's plain text.</param>
    public static int ReadingMinutes(string plainBody)
    {
        int words = plainBody.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (words + WordsPerMinute - 1) / WordsPerMinute);
    }
}
