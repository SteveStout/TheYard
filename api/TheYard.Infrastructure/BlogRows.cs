namespace TheYard.Infrastructure;

/// <summary>A row of blog.Post (ADR: Every LinkedIn post is also a blog post).</summary>
public sealed class PostRow
{
    /// <summary>The post's id, and this table's key. A surrogate here rather than the slug, because the images, tags, reads and events all point at a post and a slug is editable.</summary>
    public int Id { get; set; }

    /// <summary>The post's address on the site. Kept unique, because it is how a reader and a search engine name the post, and two posts answering at one address would be a broken page.</summary>
    public required string Slug { get; set; }

    /// <summary>Line one of the post as it went out. Kept because it is the heading the page shows and the name the admin card's rows carry.</summary>
    public required string Title { get; set; }

    /// <summary>One or two lines for a list card and the page description. Kept rather than cut from the body, because the first lines of a post are rarely what a reader needs to decide whether to open it.</summary>
    public required string Summary { get; set; }

    /// <summary>The text, as posted. Kept whole because the site is the copy that outlives the original, which is the whole point of keeping a blog beside the posts.</summary>
    public required string Body { get; set; }

    /// <summary>When it went out. Kept and indexed, because it is the order the blog is read in and the figure the stats are counted against.</summary>
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>The original on LinkedIn. Kept unique, because it is what makes a save of the same post an update rather than a second copy.</summary>
    public required string LinkedInUrl { get; set; }

    /// <summary>The code or live page the post talks about, or null when it names none. Kept because a click through to it is one of the things the funnel counts.</summary>
    public string? CodeUrl { get; set; }

    /// <summary>Whether the post is shown. Kept so a row can be written before it is ready: every read of the blog asks for published rows only, so an unpublished one is invisible without being deleted.</summary>
    public bool IsPublished { get; set; }

    /// <summary>When the row was first written. Kept apart from the published date because a post may be saved here before or long after it went out, and the two questions have different answers.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row was last written. Kept because a save of a post that already exists is an update, and without this there would be no record that it had been revised.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of blog.PostImage: one image of a post, in its place.</summary>
public sealed class PostImageRow
{
    /// <summary>The post it belongs to. Half of this row's key, and a foreign key that takes its images with it when a post goes.</summary>
    public int PostId { get; set; }

    /// <summary>Its place in the post, from one. The other half of the key, because a carousel is an order and a set of images with no order is a different post.</summary>
    public int Position { get; set; }

    /// <summary>Where the file is served. Kept because the image itself lives outside this database and this is the only pointer to it.</summary>
    public required string Url { get; set; }

    /// <summary>What the image shows, in words. Kept because a reader using a screen reader gets the post from this and from nothing else.</summary>
    public required string AltText { get; set; }

    /// <summary>Its width in pixels. Kept so the page can hold the image's space before the file arrives, which is what stops the text jumping as it loads.</summary>
    public int Width { get; set; }

    /// <summary>Its height in pixels. Kept for the same reason as the width beside it: the space is reserved from the pair.</summary>
    public int Height { get; set; }
}

/// <summary>A row of blog.PostTag.</summary>
public sealed class PostTagRow
{
    /// <summary>The post. Half of this row's key, and a foreign key that takes its tags with it when a post goes.</summary>
    public int PostId { get; set; }

    /// <summary>One hashtag, without the hash. The other half of the key, which is what makes the same tag on one post a single row however many times the post says it.</summary>
    public required string Tag { get; set; }
}

/// <summary>A row of blog.PostView, the read log.</summary>
public sealed class PostViewRow
{
    /// <summary>The read's own id, and this table's key. A log has no natural key: two readers can do the same thing in the same second, and both reads are real.</summary>
    public long Id { get; set; }

    /// <summary>The post read. Indexed with the time beside it, because every question asked of this table is about one post over a window.</summary>
    public int PostId { get; set; }

    /// <summary>When the read happened. The other half of that index, and the column every window in the stats is cut on.</summary>
    public DateTimeOffset ViewedAt { get; set; }

    /// <summary>Where the read came from: linkedin, ref, search or direct. Kept as the one word the rules settled on, so the card can count by source without re-deciding what a referrer means.</summary>
    public required string Source { get; set; }

    /// <summary>The tagged link's code, or null when the read did not come through one. Kept because it is what ties a read back to a link that was handed out, which is the only way a share can be measured.</summary>
    public string? RefCode { get; set; }

    /// <summary>The host the reader came from, or null for none. Kept as the host alone and never the whole address, because the host answers where readers come from and the rest is the reader's own business.</summary>
    public string? ReferrerHost { get; set; }

    /// <summary>The utm_source the address carried, or null for none. Kept as the campaign tagging sent it, so a campaign's own reads can be told from the rest.</summary>
    public string? UtmSource { get; set; }

    /// <summary>The utm_medium, or null for none. Kept beside the source for the same reason: the three utm columns are only useful together.</summary>
    public string? UtmMedium { get; set; }

    /// <summary>The utm_campaign, or null for none. The third of the three, and the one that names which send a read belongs to.</summary>
    public string? UtmCampaign { get; set; }

    /// <summary>What the read was on: phone, tablet, desk or bot. Kept as the class rather than the user agent, because the class is the answer and a user agent string is a record of the reader.</summary>
    public required string Device { get; set; }

    /// <summary>The edge's two-letter country, or null when it sent none. Kept at country only, which is coarse enough to say where readers are without saying who they are.</summary>
    public string? Country { get; set; }

    /// <summary>The day's keyed hash of the reader, thirty-two bytes. Kept instead of anything that identifies a person: it counts distinct readers within a day and stops being comparable the next, which is the point.</summary>
    public required byte[] SessionHash { get; set; }

    /// <summary>Whether the read looked like a bot. Kept rather than dropped, because a read that was thrown away cannot later be counted differently, and every number the card shows leaves the bots out.</summary>
    public bool IsBot { get; set; }
}

/// <summary>A row of blog.PostEvent, the event log.</summary>
public sealed class PostEventRow
{
    /// <summary>The event's own id, and this table's key, for the same reason the read log has one: two readers can do the same thing at once.</summary>
    public long Id { get; set; }

    /// <summary>The post. Indexed with the kind beside it, because the funnel asks for one post's events of one kind at a time.</summary>
    public int PostId { get; set; }

    /// <summary>The same day's keyed hash the reader's read was kept under. Kept so a depth and a time from one reader count once between them, which is what makes the funnel a count of readers rather than of events.</summary>
    public required byte[] SessionHash { get; set; }

    /// <summary>When it happened. Kept because the order of a reader's events is what tells reading a post from clicking straight out of it.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>What the reader did: depth, time, more, slide, click or copy. The other half of this table's index, and the word that says how to read the value beside it.</summary>
    public required string Type { get; set; }

    /// <summary>The figure for that kind: a quarter reached, a count of seconds, a slide's number, or where a click went. Kept as text because the six kinds do not share a unit, and the kind beside it says which one this is.</summary>
    public required string Value { get; set; }

    /// <summary>Whether the reader looked like a bot. Kept for the same reason as on a read: the numbers leave bots out, and a row thrown away could never be counted another way.</summary>
    public bool IsBot { get; set; }
}
