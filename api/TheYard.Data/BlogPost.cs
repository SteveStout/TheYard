namespace TheYard.Data;

// #region blog-post
/// <summary>
/// One post on the blog, as it was published on LinkedIn (ADR: Every LinkedIn post is also a blog post):
/// its line 1 as the title, a short summary for the list and the page description, the full text, when
/// it went out and where, the code or live page it talks about, its images in order and its tags.
/// </summary>
/// <param name="Id">The post's id in the store.</param>
/// <param name="Slug">Its address on the site.</param>
/// <param name="Title">Line 1 of the LinkedIn post.</param>
/// <param name="Summary">One or two lines for a list card and the page description.</param>
/// <param name="Body">The text, as posted.</param>
/// <param name="PublishedAt">When it went out on LinkedIn.</param>
/// <param name="LinkedInUrl">The original on LinkedIn.</param>
/// <param name="CodeUrl">The code or live page it talks about; null for none.</param>
/// <param name="Images">Its images in order: the carousel's slides first.</param>
/// <param name="Tags">Its hashtags, without the hash.</param>
public sealed record BlogPost(int Id, string Slug, string Title, string Summary, string Body, DateTimeOffset PublishedAt, string LinkedInUrl, string? CodeUrl, IReadOnlyList<BlogImage> Images, IReadOnlyList<string> Tags);

/// <summary>One image of a post: where the file is, how big, and what it says to a screen reader.</summary>
/// <param name="Position">Its place in the post, from 1.</param>
/// <param name="Url">Where the file is served.</param>
/// <param name="AltText">What it shows, in words.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public sealed record BlogImage(int Position, string Url, string AltText, int Width, int Height);

/// <summary>One read of a post as the log keeps it: when, from where, on what, and the day's keyed hash.</summary>
/// <param name="PostId">The post read.</param>
/// <param name="At">When.</param>
/// <param name="Source">linkedin, ref, search or direct.</param>
/// <param name="RefCode">The tagged link's code; null when the read did not come through one.</param>
/// <param name="ReferrerHost">The host the reader came from; null for none.</param>
/// <param name="UtmSource">The utm_source the address carried; null for none.</param>
/// <param name="UtmMedium">The utm_medium; null for none.</param>
/// <param name="UtmCampaign">The utm_campaign; null for none.</param>
/// <param name="Device">phone, tablet, desk or bot.</param>
/// <param name="Country">The edge's two-letter country; null when it sent none.</param>
/// <param name="Session">The day's keyed hash of the reader, 32 bytes.</param>
/// <param name="IsBot">True when the read looked like a bot.</param>
public sealed record BlogRead(int PostId, DateTimeOffset At, string Source, string? RefCode, string? ReferrerHost, string? UtmSource, string? UtmMedium, string? UtmCampaign, string Device, string? Country, byte[] Session, bool IsBot);

/// <summary>One thing a reader did on a post.</summary>
/// <param name="PostId">The post.</param>
/// <param name="Session">The same day's keyed hash its read was kept under.</param>
/// <param name="At">When.</param>
/// <param name="Type">depth, time, more, slide, click or copy.</param>
/// <param name="Value">25/50/75/100, seconds, the slide's number, or where the click went.</param>
/// <param name="IsBot">True when the reader looked like a bot.</param>
public sealed record BlogEvent(int PostId, byte[] Session, DateTimeOffset At, string Type, string Value, bool IsBot);

/// <summary>One post's numbers, as blog.PostStats and blog.Funnel give them, bots left out.</summary>
/// <param name="PostId">The post.</param>
/// <param name="Title">Its title.</param>
/// <param name="PublishedAt">When it went out.</param>
/// <param name="Reads">Every read.</param>
/// <param name="Readers">Distinct day-hashes.</param>
/// <param name="Reads7d">Reads in the last seven days.</param>
/// <param name="FromTaggedLinks">Reads that came through a tagged link.</param>
/// <param name="LastRead">The newest read; null for none.</param>
/// <param name="AverageDepth">The deepest quarter reached, averaged over readers; null for no depth event.</param>
/// <param name="AverageSeconds">The longest time open, averaged over readers; null for no time event.</param>
/// <param name="ToLinkedIn">Readers who clicked through to LinkedIn.</param>
/// <param name="ToCode">Readers who clicked through to the code.</param>
/// <param name="PastHalf">Readers who scrolled past half.</param>
/// <param name="Finished">Readers who reached the end.</param>
/// <param name="ClickedOut">Readers who clicked any way out.</param>
public sealed record BlogStat(int PostId, string Title, DateTimeOffset PublishedAt, int Reads, int Readers, int Reads7d, int FromTaggedLinks, DateTimeOffset? LastRead, double? AverageDepth, double? AverageSeconds, int ToLinkedIn, int ToCode, int PastHalf, int Finished, int ClickedOut);

/// <summary>Reads of the blog by source, bots left out.</summary>
/// <param name="Source">linkedin, ref, search or direct.</param>
/// <param name="Reads">How many.</param>
public sealed record BlogSourceCount(string Source, int Reads);
// #endregion blog-post
