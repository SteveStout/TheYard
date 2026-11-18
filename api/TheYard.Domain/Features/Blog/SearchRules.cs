// How a search ranks a post (ADR: Every LinkedIn post is also a blog post, the addendum on the read model): each
// typed word is widened (KeywordRules.Expand), and for each word the best place any of its terms lands is what it
// scores, title 6, a keyword 5, a topic 4, the summary 3, and the body 0.6 a mention up to five mentions plus 0.4.
// Every typed word has to land somewhere, or the post does not match. The scores are kept in tenths, so two posts
// with the same score are equal exactly and the tie goes to the newer one. The store hands in the counts; this
// file is the only place the weights live, so SQLite, Azure SQL and the memory store rank the same way.
namespace TheYard.Domain.Features.Blog;

/// <summary>A post's score for a search, in tenths, and where its words landed, in the order the match line names them.</summary>
/// <param name="Tenths">The score, ten to a point.</param>
/// <param name="Why">title, a keyword's label, a topic's label, summary, and the mentions in the text.</param>
public sealed record SearchScore(int Tenths, IReadOnlyList<string> Why);

/// <summary>The search's weights and the one rule that applies them.</summary>
public static class SearchRules
{
    // #region weights
    /// <summary>A term in the title, in tenths.</summary>
    public const int TitleWeight = 60;

    /// <summary>A term that is one of the post's keywords, in tenths.</summary>
    public const int KeywordWeight = 50;

    /// <summary>A term that is one of the post's topics, in tenths.</summary>
    public const int TopicWeight = 40;

    /// <summary>A term in the summary, in tenths.</summary>
    public const int SummaryWeight = 30;

    /// <summary>The body's weight for each mention, in tenths, up to MentionsCounted of them, plus BodyFloor.</summary>
    public const int MentionWeight = 6, MentionsCounted = 5, BodyFloor = 4;
    // #endregion weights

    /// <summary>
    /// A post's score for the typed words, or null when one of them lands nowhere. mentions answers how many times
    /// a term is in a field (BlogText.Title, Summary or Body), from the post's kept words.
    /// </summary>
    /// <param name="words">The typed words (KeywordRules.QueryWords).</param>
    /// <param name="topics">The post's topics' keys.</param>
    /// <param name="keywords">The post's keywords' keys.</param>
    /// <param name="mentions">How many times a term is in one of the post's fields.</param>
    /// <param name="expand">What a typed word is widened to: KeywordRules.Expand, or the store's copy of it in blog.SearchSynonym.</param>
    public static SearchScore? Score(IReadOnlyList<string> words, IReadOnlyList<string> topics, IReadOnlyList<string> keywords, Func<string, byte, int> mentions, Func<string, IReadOnlyList<string>>? expand = null)
    {
        expand ??= KeywordRules.Expand;
        if (words.Count == 0)
        {
            return null;
        }
        var why = new List<string>();
        void Because(string place)
        {
            if (!why.Contains(place))
            {
                why.Add(place);
            }
        }
        int total = 0, inText = 0;
        var keywordLabels = KeywordRules.Keywords.Where(k => keywords.Contains(k.Key)).Select(k => k.Label).ToList();
        var topicLabels = KeywordRules.Topics.Where(t => topics.Contains(t.Key)).Select(t => t.Label).ToList();
        foreach (string word in words)
        {
            int best = 0;
            foreach (string term in expand(word))
            {
                if (mentions(term, BlogText.Title) > 0)
                {
                    best = Math.Max(best, TitleWeight);
                    Because("title");
                }
                foreach (string label in keywordLabels.Where(label => InLabel(label, term)))
                {
                    best = Math.Max(best, KeywordWeight);
                    Because(label);
                }
                foreach (string label in topicLabels.Where(label => InLabel(label, term)))
                {
                    best = Math.Max(best, TopicWeight);
                    Because(label);
                }
                if (mentions(term, BlogText.Summary) > 0)
                {
                    best = Math.Max(best, SummaryWeight);
                    Because("summary");
                }
                int n = mentions(term, BlogText.Body);
                if (n > 0)
                {
                    best = Math.Max(best, (Math.Min(n, MentionsCounted) * MentionWeight) + BodyFloor);
                    inText = Math.Max(inText, n);
                }
            }
            if (best == 0)
            {
                return null;
            }
            total += best;
        }
        if (inText > 0)
        {
            Because(inText == 1 ? "1 mention in the text" : $"{inText} mentions in the text");
        }
        return new SearchScore(total, why);
    }

    /// <summary>Whether a term lands in a label, read the way a field is read.</summary>
    private static bool InLabel(string label, string term) =>
        BlogText.Mentions(BlogText.Counted(label, BlogText.Title).Select(w => (w.Word, w.Mentions)), term) > 0;
}
