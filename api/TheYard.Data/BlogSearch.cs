namespace TheYard.Data;

// #region blog-list
/// <summary>
/// What the list is asked for (ADR: Every LinkedIn post is also a blog post, the addendum on the read model):
/// the typed text, a topic, a keyword, a month or a year, the order, and where the page starts. Every part is
/// optional; an empty filter is every published post, newest first.
/// </summary>
/// <param name="Q">The typed text; null or blank for none.</param>
/// <param name="Topic">A topic's key (architecture, ai); null for any.</param>
/// <param name="Keyword">A keyword's key (copilot, sql); null for any.</param>
/// <param name="Month">A month as yyyy-MM; null for any.</param>
/// <param name="Year">A year; null for any.</param>
/// <param name="Sort">new (newest first, or best match while searching) or read (most read).</param>
/// <param name="After">The cursor the previous page handed back; null for the first page.</param>
public sealed record BlogFilter(string? Q, string? Topic, string? Keyword, string? Month, int? Year, string Sort, string? After);

/// <summary>One post as a card shows it: no body, the cover slide, the counts, and why it matched when searching.</summary>
/// <param name="Id">The post's id in the store.</param>
/// <param name="Slug">Its address on the blog.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its two lines.</param>
/// <param name="PublishedAt">When it went out on LinkedIn.</param>
/// <param name="Cover">Its first slide; null for none.</param>
/// <param name="Slides">How many slides it has.</param>
/// <param name="ReadingMinutes">Its words over 230, rounded up.</param>
/// <param name="Topics">Its topics' keys.</param>
/// <param name="Keywords">Its keywords' keys.</param>
/// <param name="Reads">Every read, bots left out.</param>
/// <param name="Reads7d">Reads in the last seven days, bots left out.</param>
/// <param name="Matched">Where a search landed (title, a keyword, a topic, summary, mentions in the text); empty when not searching.</param>
public sealed record BlogCard(int Id, string Slug, string Title, string Summary, DateTimeOffset PublishedAt, BlogImage? Cover, int Slides, int ReadingMinutes, IReadOnlyList<string> Topics, IReadOnlyList<string> Keywords, int Reads, int Reads7d, IReadOnlyList<string> Matched);

/// <summary>One chip or one choice of the filter bar, with how many posts it would leave.</summary>
/// <param name="Key">What the address carries.</param>
/// <param name="Label">What the chip says.</param>
/// <param name="Count">How many posts the other filters leave that have it.</param>
public sealed record BlogFacet(string Key, string Label, int Count);

/// <summary>One page of the list: its cards, how many posts match in all, the chips' counts, and the next page's cursor.</summary>
/// <param name="Cards">Up to a page of cards, in the asked order.</param>
/// <param name="Total">How many published posts match the filter.</param>
/// <param name="Topics">Each topic with its count.</param>
/// <param name="Keywords">Each keyword some post has, with its count.</param>
/// <param name="Months">Each month a post went out in, newest first, with its count.</param>
/// <param name="Next">The cursor for the next page; null on the last.</param>
/// <param name="Terms">What the typed words were widened to and matched as, for the page to mark; empty when not searching.</param>
public sealed record BlogListPage(IReadOnlyList<BlogCard> Cards, int Total, IReadOnlyList<BlogFacet> Topics, IReadOnlyList<BlogFacet> Keywords, IReadOnlyList<BlogFacet> Months, string? Next, IReadOnlyList<string>? Terms = null);
// #endregion blog-list

/// <summary>One word of a post as the search keeps it: the word or two neighbouring words, where, and how often.</summary>
/// <param name="Word">Lower case; two words have one space between.</param>
/// <param name="Field">1 the title, 2 the summary, 3 the body.</param>
/// <param name="Mentions">How many times.</param>
public sealed record BlogWord(string Word, byte Field, int Mentions);

/// <summary>One keyword a post really talks about, and how many times it names it.</summary>
/// <param name="Key">The keyword's key.</param>
/// <param name="Mentions">How many times.</param>
public sealed record BlogKeywordCount(string Key, int Mentions);

/// <summary>What a save works out from a post, kept beside it: the body drawn once, the minutes, the words and the keywords.</summary>
/// <param name="BodyHtml">The body as HTML.</param>
/// <param name="ReadingMinutes">The body's words over 230, rounded up.</param>
/// <param name="Words">The words the search reads.</param>
/// <param name="Keywords">The keywords the chips read.</param>
/// <param name="Version">The rules' version it was worked out with.</param>
public sealed record BlogDerived(string BodyHtml, int ReadingMinutes, IReadOnlyList<BlogWord> Words, IReadOnlyList<BlogKeywordCount> Keywords, int Version);

/// <summary>One post as its own page shows it: the post, its body drawn, its counts, and where to go next.</summary>
/// <param name="Post">The post, its source body included.</param>
/// <param name="BodyHtml">The body drawn to HTML; empty when no save has drawn it yet.</param>
/// <param name="ReadingMinutes">Its words over 230, rounded up.</param>
/// <param name="Topics">Its topics' keys.</param>
/// <param name="Keywords">Its keywords' keys.</param>
/// <param name="Reads">Every read, bots left out.</param>
/// <param name="Reads7d">Reads in the last seven days, bots left out.</param>
/// <param name="Older">The post before it; null for the first.</param>
/// <param name="Newer">The post after it; null for the newest.</param>
/// <param name="Related">Up to three other posts that share a topic with it, newest first.</param>
public sealed record BlogPostPage(BlogPost Post, string BodyHtml, int ReadingMinutes, IReadOnlyList<string> Topics, IReadOnlyList<string> Keywords, int Reads, int Reads7d, BlogCard? Older, BlogCard? Newer, IReadOnlyList<BlogCard> Related);
