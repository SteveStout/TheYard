// The parts of a list page both stores shape the same way once they have their counts: the page cut from the
// places in order, and the three rows of chips with their labels. The relational store counts in SQL and the
// memory store in LINQ; what the counts become is written once, here, so the two answers cannot drift apart.
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

/// <summary>Cuts a page from places in order, and labels the chips.</summary>
public static class BlogListShape
{
    /// <summary>The places after the cursor, a page of them, and the cursor of the next page when more follow.</summary>
    /// <param name="places">The places of every post that matches, in any order.</param>
    /// <param name="after">The previous page's last place; null for the first page.</param>
    public static (IReadOnlyList<BlogCursor> Page, string? Next) Page(IEnumerable<BlogCursor> places, BlogCursor? after)
    {
        var rest = BlogListRules.Ordered(places).Where(place => after is null || BlogListRules.After(place, after)).Take(BlogListRules.PageSize + 1).ToList();
        return Cut(rest);
    }

    /// <summary>A page from places already in order and already after the cursor, with one more read when there is one.</summary>
    /// <param name="rest">At most a page and one more.</param>
    public static (IReadOnlyList<BlogCursor> Page, string? Next) Cut(IReadOnlyList<BlogCursor> rest)
    {
        var page = rest.Take(BlogListRules.PageSize).ToList();
        return (page, rest.Count > BlogListRules.PageSize ? BlogListRules.Write(page[^1]) : null);
    }

    /// <summary>Every topic in the chips' order with its count.</summary>
    /// <param name="counts">How many matching posts have each topic.</param>
    public static IReadOnlyList<BlogFacet> Topics(IReadOnlyDictionary<string, int> counts) =>
        [.. KeywordRules.Topics.Select(t => new BlogFacet(t.Key, t.Label, counts.GetValueOrDefault(t.Key)))];

    /// <summary>The keywords some published post has, most posts first, each with its count among the matching posts.</summary>
    /// <param name="havingAll">How many published posts have each keyword.</param>
    /// <param name="counts">How many matching posts have each keyword.</param>
    public static IReadOnlyList<BlogFacet> Keywords(IReadOnlyDictionary<string, int> havingAll, IReadOnlyDictionary<string, int> counts) =>
        [.. BlogListRules.KeywordChips(havingAll).Select(k => new BlogFacet(k.Key, k.Label, counts.GetValueOrDefault(k.Key)))];

    /// <summary>Every month a published post went out in, newest first, each with how many matching posts went out in it.</summary>
    /// <param name="published">When every published post went out.</param>
    /// <param name="matching">When each matching post went out.</param>
    public static IReadOnlyList<BlogFacet> Months(IEnumerable<DateTimeOffset> published, IEnumerable<DateTimeOffset> matching)
    {
        var counts = matching.GroupBy(BlogListRules.MonthOf).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return [.. published.Select(BlogListRules.MonthOf).Distinct(StringComparer.Ordinal).OrderDescending(StringComparer.Ordinal)
            .Select(month => new BlogFacet(month, BlogListRules.MonthLabel(month), counts.GetValueOrDefault(month)))];
    }

    /// <summary>The post's topics the list knows, in the chips' order; a tag that is no topic is left out.</summary>
    /// <param name="tags">The post's tags.</param>
    public static IReadOnlyList<string> TopicsOf(IEnumerable<string> tags)
    {
        var have = tags.ToHashSet(StringComparer.Ordinal);
        return [.. KeywordRules.Topics.Select(t => t.Key).Where(have.Contains)];
    }

    /// <summary>The post's keywords in the list's order.</summary>
    /// <param name="keys">The keyword keys kept for it.</param>
    public static IReadOnlyList<string> KeywordsOf(IEnumerable<string> keys)
    {
        var have = keys.ToHashSet(StringComparer.Ordinal);
        return [.. KeywordRules.Keywords.Select(k => k.Key).Where(have.Contains)];
    }
}
