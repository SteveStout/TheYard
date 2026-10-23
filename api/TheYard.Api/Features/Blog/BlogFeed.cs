using System.Globalization;
using System.Xml.Linq;
using TheYard.Application.Features.Blog;
using TheYard.Data;

namespace TheYard.Api.Features.Blog;

/// <summary>
/// The blog as an RSS 2.0 feed (ADR: Every LinkedIn post is also a blog post): every published post,
/// newest first, each with its address on the blog, its date and its two-line summary, so a reader can
/// follow the blog without LinkedIn. Written from the same handler the page reads.
/// </summary>
public static class BlogFeed
{
    /// <summary>Maps the feed's route.</summary>
    public static IEndpointRouteBuilder MapBlogFeed(this IEndpointRouteBuilder app)
    {
        // #region blog-feed
        app.MapGet("/api/blog/rss.xml", Feed)
            .WithName("GetBlogFeed").WithTags("Blog")
            .WithSummary("Every published post as an RSS 2.0 feed, newest first")
            .Produces<string>(StatusCodes.Status200OK, "application/rss+xml")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        // #endregion blog-feed
        return app;
    }

    private static async Task<IResult> Feed(HttpContext http, SiteHosts hosts, ListPostsHandler handler, CancellationToken cancellation)
    {
        var (posts, _, unavailable) = await handler.HandleAsync(new ListPostsQuery(), cancellation);
        return unavailable is { } reason
            ? Results.Problem(detail: reason, statusCode: 503, title: "The blog cannot be read now")
            : Results.Text(Write(posts, hosts.LinkOrigin(http)), "application/rss+xml; charset=utf-8");
    }

    /// <summary>The feed's text for these posts, in the order given.</summary>
    /// <param name="posts">The posts, newest first.</param>
    /// <param name="site">The origin the links point at: the Yard site the request arrived on.</param>
    public static string Write(IReadOnlyList<BlogPost> posts, string site)
    {
        var channel = new XElement(
            "channel",
            new XElement("title", "TheYard blog, Steven Stout"),
            new XElement("link", $"{site}/?view=blog"),
            new XElement("description", "Every LinkedIn post Steven Stout publishes, kept in full with the code it talks about."),
            new XElement("language", "en-us"));
        foreach (var post in posts)
        {
            string link = $"{site}/?view=blog&post={post.Slug}";
            channel.Add(new XElement(
                "item",
                new XElement("title", post.Title),
                new XElement("link", link),
                new XElement("guid", new XAttribute("isPermaLink", "true"), link),
                new XElement("pubDate", post.PublishedAt.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)),
                new XElement("description", post.Summary),
                post.Tags.Select(tag => new XElement("category", tag))));
        }
        var feed = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("rss", new XAttribute("version", "2.0"), channel));
        return feed.Declaration + "\n" + feed.Root!.ToString();
    }
}
