using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>A TheYard blog post as the public feed lists it.</summary>
/// <param name="Slug">The post's address on the blog.</param>
/// <param name="Title">The post's title.</param>
/// <param name="PublishedAt">When it went out.</param>
public sealed record BlogPost(string Slug, string Title, DateTimeOffset PublishedAt);

/// <summary>The post a follow-up names, and its tagged link.</summary>
/// <param name="Title">The post's title, as the feed gives it.</param>
/// <param name="Link">The post's address carrying the row's code.</param>
public sealed record BlogMention(string Title, string Link)
{
    /// <summary>The sentence the template carries after the TopicLine, with its leading space.</summary>
    public string Sentence => $" Since then I wrote up {Title}: {Link}";
}

/// <summary>
/// The follow-up's one optional line: a post Steve wrote since he last wrote to the person, with its tagged link. The
/// 4 AM lane picks the post from the blog's feed and mints the code with make_ref.py; this only checks what it was
/// handed and builds the post's address around it, so the Function mints nothing. The line goes in only when the
/// row holds both, the post is on the feed, and it went out after Steve's last message to the person.
/// </summary>
public static partial class BlogLine
{
    /// <summary>The site the blog lives on.</summary>
    public const string Site = "https://theyard.stevenstout.biz";

    /// <summary>How many days before a row is due the lane is asked for its post and code.</summary>
    public const int LeadDays = 3;

    /// <summary>The longest slug the row's column holds.</summary>
    public const int LongestSlug = 200;

    /// <summary>The check a slug and code fail, or null when the row may store them.</summary>
    /// <param name="slug">The post's slug, as the feed gives it.</param>
    /// <param name="code">The code make_ref.py printed.</param>
    public static string? Fails(string? slug, string? code)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > LongestSlug || !Slug().IsMatch(slug))
        {
            return "slug";
        }
        return code is not null && Code().IsMatch(code) ? null : "code";
    }

    /// <summary>The post's address with the row's code on it.</summary>
    public static string Link(string slug, string code) => $"{Site}/?view=blog&post={slug}&r={code}";

    /// <summary>
    /// True when the 4 AM lane should look for a post and a code for the row: an Open email row with neither stored,
    /// from <see cref="LeadDays"/> before it is due until it goes stale.
    /// </summary>
    public static bool Needed(Conversation row, DateTimeOffset now) =>
        row.State == ConversationState.Open && row.LastChannel == Channel.Email && row.BlogSlug is null && row.RefCode is null
        && row.LastExchangeAt is { } last && now >= last.AddDays(DueRule.Days - LeadDays) && now <= last.AddDays(DueRule.StaleDays);

    /// <summary>The post the row's follow-up names, or null when the row holds no slug and code or the post is not newer than Steve's last message.</summary>
    public static BlogMention? For(Conversation row, IEnumerable<BlogPost> posts)
    {
        if (row.BlogSlug is not { } slug || row.RefCode is not { } code || Fails(slug, code) is not null)
        {
            return null;
        }
        var post = posts.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.Ordinal));
        if (post is null || string.IsNullOrWhiteSpace(post.Title) || (row.LastOutboundAt is { } sent && post.PublishedAt <= sent))
        {
            return null;
        }
        return new BlogMention(post.Title.Trim(), Link(slug, code));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Slug();

    [GeneratedRegex("^[a-z0-9]{6}$")]
    private static partial Regex Code();
}
