// The blog's topics, keywords and synonyms (ADR: Every LinkedIn post is also a blog post, the addendum on the read
// model), in one place with its own tests. The topic and keyword lists are the owner's: they change on the owner's word, and
// the backfill's topics are the mockup he reviewed. A keyword is a named product or idea a post really talks about:
// it is kept when the title names it, or when the words a reader reads (link text, never a link's address) name it
// at least its minimum. The synonyms widen a typed word before it is matched, the mockup's list whole, and a short
// alias stands only for what it names, so ms finds Microsoft and never "39 ms".
namespace TheYard.Domain.Features.Blog;

/// <summary>A topic a post is filed under: the key the address carries and the label the chip shows, never with a "#".</summary>
/// <param name="Key">The key.</param>
/// <param name="Label">The label.</param>
public sealed record BlogTopic(string Key, string Label);

/// <summary>A keyword: its key, its label, the whole words that count as a mention of it, and the mentions it needs.</summary>
/// <param name="Key">The key the address carries.</param>
/// <param name="Label">The label the chip shows.</param>
/// <param name="Aliases">The words that are a mention, lower case; a plural with an s counts too.</param>
/// <param name="Minimum">The mentions the text needs when the title does not name it.</param>
/// <param name="CaseMatters">True when only the capitals count (SOLID, and not "a solid plan").</param>
public sealed record BlogKeyword(string Key, string Label, IReadOnlyList<string> Aliases, int Minimum, bool CaseMatters = false);

/// <summary>The blog's topics, keywords and synonyms, and the rules that read them.</summary>
public static class KeywordRules
{
    /// <summary>The topics, in the order the chips show them.</summary>
    public static readonly IReadOnlyList<BlogTopic> Topics =
    [
        new("ai", "AI"), new("architecture", "Architecture"), new("dotnet", ".NET"), new("azure", "Azure"),
        new("cost", "Cloud cost"), new("testing", "Testing"), new("linux", "Linux"), new("security", "Security"),
        new("career", "Career"), new("job-search", "Job search"),
    ];

    // #region keyword-list
    /// <summary>The keywords, in the order a post lists them. The minimums reproduce the mockup's assignment over the words a reader reads.</summary>
    public static readonly IReadOnlyList<BlogKeyword> Keywords =
    [
        new("microsoft", "Microsoft", ["microsoft", "msft"], 3),
        new("copilot", "Microsoft Copilot", ["copilot"], 3),
        new("claude", "Claude", ["claude"], 3),
        new("fable", "Claude Fable", ["fable"], 3),
        new("coderabbit", "CodeRabbit", ["coderabbit"], 3),
        new("openai", "OpenAI", ["openai"], 3),
        new("github", "GitHub", ["github"], 3),
        new("sql", "SQL Server", ["sql"], 4),
        new("cosmos", "Cosmos DB", ["cosmos"], 3),
        new("blob", "Blob Storage", ["blob"], 3),
        new("postgres", "Postgres", ["postgres", "postgresql"], 3),
        new("tailscale", "Tailscale", ["tailscale"], 1),
        new("cqrs", "CQRS", ["cqrs"], 2),
        new("onion", "Onion architecture", ["onion"], 3),
        new("solid", "SOLID", ["solid"], 3, CaseMatters: true),
    ];
    // #endregion keyword-list

    // #region synonyms
    /// <summary>What a typed word is widened to, the mockup's list: the word itself is always one of its terms, unless it is an alias only.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["microsoft"] = ["microsoft", "msft"],
        ["ms"] = ["microsoft"],
        ["msft"] = ["microsoft"],
        ["copilot"] = ["copilot", "microsoft copilot", "github copilot"],
        ["ai"] = ["ai", "llm", "agent", "agents", "artificial intelligence", "claude", "copilot", "openai", "genai"],
        ["llm"] = ["llm", "ai", "claude", "copilot", "openai"],
        ["claude"] = ["claude", "anthropic", "fable", "opus"],
        ["anthropic"] = ["anthropic", "claude"],
        ["security"] = ["security", "scam", "scams", "scammer", "scammers", "phishing", "fraud", "red flag", "fake"],
        ["scam"] = ["scam", "scams", "scammer", "scammers", "fraud", "phishing", "fake"],
        ["dotnet"] = [".net", "dotnet", "c#", "csharp"],
        ["net"] = [".net", "dotnet"],
        ["csharp"] = ["c#", "csharp"],
        ["sql"] = ["sql", "sql server", "azure sql", "database", "query"],
        ["database"] = ["database", "sql", "cosmos", "postgres"],
        ["cost"] = ["cost", "costs", "bill", "billing", "spend", "spike", "dollar"],
        ["azure"] = ["azure", "blob", "cosmos", "app service", "front door"],
        ["test"] = ["test", "tests", "testing", "gate", "coderabbit"],
        ["testing"] = ["test", "tests", "testing", "gate", "coderabbit"],
        ["linux"] = ["linux", "mint", "server", "tailscale"],
        ["job"] = ["job", "jobs", "recruiter", "resume", "hiring"],
        ["architecture"] = ["architecture", "onion", "cqrs", "solid", "clean"],
    };

    /// <summary>The terms of more than one word ("microsoft copilot", "red flag"): the only pairs of words a search can land on.</summary>
    public static readonly IReadOnlyList<string> Phrases = [.. Synonyms.Values.SelectMany(terms => terms).Where(term => term.Contains(' ')).Distinct(StringComparer.Ordinal)];

    /// <summary>Whether a pair of words is one a phrase lands on: it starts with one of the phrases.</summary>
    /// <param name="pair">Two words with one space between.</param>
    public static bool IsPhrasePair(string pair) => Phrases.Any(phrase => pair.StartsWith(phrase, StringComparison.Ordinal));

    /// <summary>The short aliases that stand only for what they name: ms is Microsoft, and is never looked for as ms.</summary>
    public static readonly IReadOnlySet<string> AliasOnly = new HashSet<string>(StringComparer.Ordinal) { "ms", "msft" };
    // #endregion synonyms

    /// <summary>
    /// The version of these rules and of the cut BlogText makes. A post's derived words, keywords, minutes and
    /// drawn body carry the version they were worked out with, and a post under an older one is derived again.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The words of a typed query that are matched, in the order typed: cut the way the text is cut, each at least
    /// two characters, a word a dot opens kept with its dot (".net") and not a second time without it.
    /// </summary>
    /// <param name="query">What the reader typed.</param>
    public static IReadOnlyList<string> QueryWords(string? query)
    {
        var cut = BlogText.Words(query ?? string.Empty);
        var words = new List<string>();
        for (int i = 0; i < cut.Count; i++)
        {
            words.Add(cut[i].Word);
            if (cut[i].Dotted)
            {
                i++;
            }
        }
        return [.. words.Where(word => word.Length > 1)];
    }

    /// <summary>
    /// The terms one typed word is matched as: the word, its synonyms, and the synonyms of the word without a final
    /// s, so scams widens as scam does; an alias only is its synonyms and never itself.
    /// </summary>
    /// <param name="word">One typed word, lower case.</param>
    public static IReadOnlyList<string> Expand(string word)
    {
        if (AliasOnly.Contains(word))
        {
            return Synonyms[word];
        }
        var terms = new List<string> { word };
        terms.AddRange(Synonyms.GetValueOrDefault(word) ?? []);
        if (word.EndsWith('s'))
        {
            terms.AddRange(Synonyms.GetValueOrDefault(word[..^1]) ?? []);
        }
        return [.. terms.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// blog.SearchSynonym's rows: every word the list widens, and the same word with an s, each with every term
    /// Expand gives it. A typed word with no row is matched as itself, which is what Expand gives it too.
    /// </summary>
    public static IReadOnlyList<(string Word, string Term)> SynonymRows() =>
        [.. Synonyms.Keys.SelectMany(key => new[] { key, key + "s" }).Distinct(StringComparer.Ordinal)
            .SelectMany(word => Expand(word).Select(term => (word, term)))
            .Distinct()];

    /// <summary>The keywords a post really talks about, in the list's order, each with its mentions in the title, the summary and the body's plain text.</summary>
    /// <param name="title">The post's title.</param>
    /// <param name="summary">Its summary.</param>
    /// <param name="plainBody">Its body's plain text (BlogMarkdown.Plain).</param>
    public static IReadOnlyList<(string Key, int Mentions)> KeywordsOf(string title, string summary, string plainBody)
    {
        var found = new List<(string, int)>();
        foreach (var keyword in Keywords)
        {
            int inTitle = Mentions(keyword, title);
            int mentions = inTitle + Mentions(keyword, summary) + Mentions(keyword, plainBody);
            if (inTitle > 0 || mentions >= keyword.Minimum)
            {
                found.Add((keyword.Key, mentions));
            }
        }
        return found;
    }

    /// <summary>How many whole words of a text are one of the keyword's aliases or its plural.</summary>
    private static int Mentions(BlogKeyword keyword, string text)
    {
        var words = keyword.CaseMatters ? CaseKept(text) : BlogText.Words(text).Where(w => !w.Dotted).Select(w => w.Word);
        return words.Count(word => keyword.Aliases.Any(alias =>
        {
            string wanted = keyword.CaseMatters ? alias.ToUpperInvariant() : alias;
            return word == wanted || word == wanted + (keyword.CaseMatters ? "S" : "s");
        }));
    }

    /// <summary>The words of a text with their capitals kept, cut as BlogText cuts them.</summary>
    private static List<string> CaseKept(string text)
    {
        var kept = new List<string>();
        int at = 0;
        foreach (var (word, dotted, _) in BlogText.Words(text))
        {
            int start = dotted ? -1 : text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                at = start + word.Length;
                kept.Add(text.Substring(start, word.Length));
            }
        }
        return kept;
    }
}
