using System.Globalization;
using System.Xml.Linq;
using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

/// <summary>
/// The blog's own sitemap at /sitemap-blog.xml (ADR: Search engines are told about every post): the blog's address and
/// every published post at its canonical address, each with the day it was published, read from the store at request
/// time, so a post saved by the operator is listed with no commit. A root path because a sitemap may list only the
/// addresses at or under its own; robots.txt names it beside the static sitemap of the documents.
/// </summary>
public static class BlogSitemap
{
    /// <summary>The blog's own address, every post.</summary>
    public const string BlogPath = "/?view=blog";

    private static readonly XNamespace Urls = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>Maps the sitemap's route.</summary>
    public static IEndpointRouteBuilder MapBlogSitemap(this IEndpointRouteBuilder app)
    {
        // #region blog-sitemap
        app.MapGet("/sitemap-blog.xml", Sitemap)
            .WithName("GetBlogSitemap").WithTags("Blog")
            .WithSummary("The blog's address and every published post at its canonical address, as a sitemap")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        // #endregion blog-sitemap
        return app;
    }

    private static async Task<IResult> Sitemap(CanonicalSite site, ReadFeedHandler handler, CancellationToken cancellation)
    {
        var (posts, unavailable) = await handler.HandleAsync(new ReadFeedQuery(), cancellation);
        return unavailable is { } reason
            ? Results.Problem(detail: reason, statusCode: 503, title: "The blog cannot be read now")
            : Results.Text(Write(posts, site.Origin), "application/xml; charset=utf-8");
    }

    /// <summary>A post's canonical address, as the page's head names it: the slug escaped as a query value, never a tagged code.</summary>
    /// <param name="site">The canonical origin, with no trailing slash.</param>
    /// <param name="slug">The post's slug.</param>
    public static string PostAddress(string site, string slug) => $"{site}{BlogPath}&post={Uri.EscapeDataString(slug)}";

    /// <summary>The sitemap's text: the blog first, dated by its newest post, then each post once, in the order given.</summary>
    /// <param name="posts">The published posts, newest first.</param>
    /// <param name="site">The canonical origin, with no trailing slash.</param>
    public static string Write(IReadOnlyList<BlogPost> posts, string site)
    {
        static string Day(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var set = new XElement(Urls + "urlset");
        var blog = new XElement(Urls + "url", new XElement(Urls + "loc", site + BlogPath));
        if (posts.Count > 0)
        {
            blog.Add(new XElement(Urls + "lastmod", Day(posts.Max(post => post.PublishedAt))));
        }
        set.Add(blog);
        foreach (var post in posts.DistinctBy(post => post.Slug, StringComparer.Ordinal))
        {
            set.Add(new XElement(Urls + "url", new XElement(Urls + "loc", PostAddress(site, post.Slug)), new XElement(Urls + "lastmod", Day(post.PublishedAt))));
        }
        var sitemap = new XDocument(new XDeclaration("1.0", "UTF-8", null), set);
        return sitemap.Declaration + "\n" + sitemap.Root!.ToString();
    }
}

/// <summary>
/// The origin every canonical link is written against, the public address the edge serves (Azure:Resources:PublicSite),
/// the one render/heads.ts writes as SITE, without its trailing slash; BlogSitemapTests holds the two equal. From
/// configuration, because no name of this deployment is written in the code.
/// </summary>
/// <param name="Origin">The scheme and host, with no trailing slash.</param>
public sealed record CanonicalSite(string Origin)
{
    /// <summary>The configured public address; empty when none is set, which writes every address from the root of whichever site is read.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static CanonicalSite From(IConfiguration configuration) =>
        new((configuration["Azure:Resources:PublicSite"] ?? "").TrimEnd('/'));
}
