using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

// #region blog-wire
/// <summary>
/// The blog's answers as the page reads them, in snake_case (ADR: Every LinkedIn post is also a blog post, the addendum
/// on the read model). A card carries no body; a post carries its body drawn to HTML for the page and its source for
/// the save workflow's read-back, and every picture carries its 320 px thumbnail's address beside its own.
/// </summary>
public static class BlogWire
{
    /// <summary>One page of the list.</summary>
    /// <param name="Cards">The page's cards, in the asked order.</param>
    /// <param name="Total">How many posts match in all.</param>
    /// <param name="Topics">The topic chips with their counts.</param>
    /// <param name="Keywords">The keyword chips with their counts.</param>
    /// <param name="Months">The months with their counts, newest first.</param>
    /// <param name="Next">The next page's cursor, for ?after=; null on the last page.</param>
    /// <param name="Terms">What the typed words were widened to, for the page to mark in the titles and summaries.</param>
    public sealed record List(IReadOnlyList<Card> Cards, int Total, IReadOnlyList<Facet> Topics, IReadOnlyList<Facet> Keywords, IReadOnlyList<Facet> Months, string? Next, IReadOnlyList<string> Terms);

    /// <summary>One card.</summary>
    /// <param name="Slug">The post's address on the blog.</param>
    /// <param name="Title">Its title.</param>
    /// <param name="Summary">Its two lines.</param>
    /// <param name="PublishedAt">When LinkedIn first had it.</param>
    /// <param name="Cover">Its first slide, as the card draws it; null for none.</param>
    /// <param name="Slides">How many slides it has.</param>
    /// <param name="ReadingMinutes">Its words over 230, rounded up.</param>
    /// <param name="Topics">Its topics' keys.</param>
    /// <param name="Keywords">Its keywords' keys.</param>
    /// <param name="Reads">Every read, bots left out.</param>
    /// <param name="Reads7d">The last seven days' reads.</param>
    /// <param name="Matched">Where a search landed; empty when not searching.</param>
    public sealed record Card(string Slug, string Title, string Summary, DateTimeOffset PublishedAt, Cover? Cover, int Slides, int ReadingMinutes, IReadOnlyList<string> Topics, IReadOnlyList<string> Keywords, int Reads, int Reads7d, IReadOnlyList<string> Matched);

    /// <summary>A card's cover: the first slide's thumbnail and its size, decorative on the card, so no alt text rides along.</summary>
    /// <param name="Url">The full slide.</param>
    /// <param name="ThumbUrl">Its 320 px thumbnail.</param>
    /// <param name="Width">The slide's width.</param>
    /// <param name="Height">The slide's height.</param>
    public sealed record Cover(string Url, string ThumbUrl, int Width, int Height);

    /// <summary>One chip: what the address carries, what it says, and how many posts it would leave.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Label">The label.</param>
    /// <param name="Count">The count.</param>
    public sealed record Facet(string Key, string Label, int Count);

    /// <summary>One slide or picture of a post.</summary>
    /// <param name="Position">Its place, from 1.</param>
    /// <param name="Url">The full picture.</param>
    /// <param name="ThumbUrl">Its 320 px thumbnail.</param>
    /// <param name="AltText">What it shows.</param>
    /// <param name="Width">Its width.</param>
    /// <param name="Height">Its height.</param>
    public sealed record Image(int Position, string Url, string ThumbUrl, string AltText, int Width, int Height);

    /// <summary>One post's page.</summary>
    /// <param name="Slug">Its address on the blog.</param>
    /// <param name="Title">Its title.</param>
    /// <param name="Summary">Its two lines.</param>
    /// <param name="Body">The body as saved, which the save workflow reads back.</param>
    /// <param name="BodyHtml">The body drawn to HTML, which the page shows.</param>
    /// <param name="PublishedAt">When LinkedIn first had it.</param>
    /// <param name="LinkedinUrl">The post on LinkedIn.</param>
    /// <param name="CodeUrl">The code it talks about, when it names some.</param>
    /// <param name="Images">Its slides, in order.</param>
    /// <param name="Tags">What the operator saved as its tags: its topics.</param>
    /// <param name="Topics">Its topics the list knows.</param>
    /// <param name="Keywords">Its keywords.</param>
    /// <param name="ReadingMinutes">Its words over 230, rounded up.</param>
    /// <param name="Reads">Every read, bots left out.</param>
    /// <param name="Reads7d">The last seven days' reads.</param>
    /// <param name="Older">The post before it; null for the first.</param>
    /// <param name="Newer">The post after it; null for the newest.</param>
    /// <param name="Related">Up to three posts that share a topic with it.</param>
    public sealed record Post(string Slug, string Title, string Summary, string Body, string BodyHtml, DateTimeOffset PublishedAt, string LinkedinUrl, string? CodeUrl, IReadOnlyList<Image> Images, IReadOnlyList<string> Tags, IReadOnlyList<string> Topics, IReadOnlyList<string> Keywords, int ReadingMinutes, int Reads, int Reads7d, Card? Older, Card? Newer, IReadOnlyList<Card> Related);

    /// <summary>A list page on the wire.</summary>
    /// <param name="page">The page the store answered.</param>
    public static List Of(BlogListPage page) =>
        new([.. page.Cards.Select(Of)], page.Total, [.. page.Topics.Select(Of)], [.. page.Keywords.Select(Of)], [.. page.Months.Select(Of)], page.Next, page.Terms ?? []);

    /// <summary>A card on the wire.</summary>
    /// <param name="card">The card.</param>
    public static Card Of(BlogCard card) =>
        new(card.Slug, card.Title, card.Summary, card.PublishedAt,
            card.Cover is { } c ? new Cover(c.Url, BlogImageNames.ThumbOf(c.Url), c.Width, c.Height) : null,
            card.Slides, card.ReadingMinutes, card.Topics, card.Keywords, card.Reads, card.Reads7d, card.Matched);

    /// <summary>A post's page on the wire.</summary>
    /// <param name="page">The page the store answered.</param>
    public static Post Of(BlogPostPage page)
    {
        var p = page.Post;
        return new(p.Slug, p.Title, p.Summary, p.Body, page.BodyHtml, p.PublishedAt, p.LinkedInUrl, p.CodeUrl,
            [.. p.Images.Select(i => new Image(i.Position, i.Url, BlogImageNames.ThumbOf(i.Url), i.AltText, i.Width, i.Height))],
            p.Tags, page.Topics, page.Keywords, page.ReadingMinutes, page.Reads, page.Reads7d,
            page.Older is { } older ? Of(older) : null, page.Newer is { } newer ? Of(newer) : null, [.. page.Related.Select(Of)]);
    }

    private static Facet Of(BlogFacet facet) => new(facet.Key, facet.Label, facet.Count);
}
// #endregion blog-wire
