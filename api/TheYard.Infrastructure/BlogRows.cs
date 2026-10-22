namespace TheYard.Infrastructure;

/// <summary>A row of blog.Post (ADR: Every LinkedIn post is also a blog post).</summary>
public sealed class PostRow
{
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Title { get; set; }

    public required string Summary { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    public required string LinkedInUrl { get; set; }

    public string? CodeUrl { get; set; }

    public bool IsPublished { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of blog.PostImage: one image of a post, in its place.</summary>
public sealed class PostImageRow
{
    public int PostId { get; set; }

    public int Position { get; set; }

    public required string Url { get; set; }

    public required string AltText { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

/// <summary>A row of blog.PostTag.</summary>
public sealed class PostTagRow
{
    public int PostId { get; set; }

    public required string Tag { get; set; }
}

/// <summary>A row of blog.PostView, the read log.</summary>
public sealed class PostViewRow
{
    public long Id { get; set; }

    public int PostId { get; set; }

    public DateTimeOffset ViewedAt { get; set; }

    public required string Source { get; set; }

    public string? RefCode { get; set; }

    public string? ReferrerHost { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public string? UtmCampaign { get; set; }

    public required string Device { get; set; }

    public string? Country { get; set; }

    public required byte[] SessionHash { get; set; }

    public bool IsBot { get; set; }
}

/// <summary>A row of blog.PostEvent, the event log.</summary>
public sealed class PostEventRow
{
    public long Id { get; set; }

    public int PostId { get; set; }

    public required byte[] SessionHash { get; set; }

    public DateTimeOffset At { get; set; }

    public required string Type { get; set; }

    public required string Value { get; set; }

    public bool IsBot { get; set; }
}
