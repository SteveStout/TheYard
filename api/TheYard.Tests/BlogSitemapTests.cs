using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Blog;
using TheYard.Application.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The two crawler files the blog writes at request time (ADR: Search engines are told about every post): the blog's
/// sitemap at /sitemap-blog.xml and the plain index at /llms.txt, each listing every published post once at the address
/// the page's head names as canonical, never with a tagged code. Read from an in-memory store (RULE 79: no database).
/// </summary>
public sealed class BlogSitemapTests(BlogSitemapTests.PostsHost host) : IClassFixture<BlogSitemapTests.PostsHost>
{
    private static readonly XNamespace Urls = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>The canonical origin the test writes against, as the configuration the host reads gives it.</summary>
    private const string Site = "https://theyard.stevenstout.biz";

    private static BlogPost Post(string slug, DateTimeOffset at, string title = "A post", string summary = "Two lines about it.") =>
        new(0, slug, title, summary, "The text.\n#dotnet", at, $"https://www.linkedin.com/posts/{slug}", null, [new BlogImage(1, "/blog-images/a.png", "the first slide", 1080, 1350)], ["dotnet"]);

    private static readonly BlogPost[] Three =
    [
        Post("the-newest-post", new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero), "The newest & best", "One line.\nAnd a second."),
        Post("a-middle-post", new DateTimeOffset(2026, 11, 12, 3, 0, 0, TimeSpan.Zero)),
        Post("the-first-post", new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero)),
    ];

    /// <summary>A host whose blog store is in memory and holds the three posts.</summary>
    public sealed class PostsHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                var store = new MemoryBlogStore("in memory, for the test");
                foreach (var post in Three)
                {
                    store.SaveAsync(post, PostDerivation.Of(post.Title, post.Summary, post.Body), post.PublishedAt, CancellationToken.None).GetAwaiter().GetResult();
                }
                services.AddSingleton<IBlogStore>(store);
            });
    }

    // #region the-sitemap
    [Fact]
    public void The_sitemap_lists_the_blog_and_every_post_once_at_its_canonical_address()
    {
        var sitemap = XDocument.Parse(BlogSitemap.Write([.. Three, Three[1]], Site));
        string[] locs = sitemap.Descendants(Urls + "loc").Select(loc => loc.Value).ToArray();

        Assert.Equal(Urls + "urlset", sitemap.Root!.Name);
        Assert.Equal(
            ["https://theyard.stevenstout.biz/?view=blog", .. Three.Select(post => $"https://theyard.stevenstout.biz/?view=blog&post={post.Slug}")],
            locs);
        Assert.DoesNotContain(locs, loc => loc.Contains("r=", StringComparison.Ordinal));
        string[] days = sitemap.Descendants(Urls + "lastmod").Select(day => day.Value).ToArray();
        Assert.Equal(["2026-11-19", "2026-11-19", "2026-11-12", "2026-10-19"], days);
    }

    [Fact]
    public void The_sitemap_escapes_the_ampersand_and_names_no_day_for_an_empty_blog()
    {
        string text = BlogSitemap.Write(Three, Site);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text, StringComparison.Ordinal);
        Assert.Contains("<loc>https://theyard.stevenstout.biz/?view=blog&amp;post=a-middle-post</loc>", text, StringComparison.Ordinal);

        var empty = XDocument.Parse(BlogSitemap.Write([], Site));
        Assert.Single(empty.Descendants(Urls + "url"));
        Assert.Empty(empty.Descendants(Urls + "lastmod"));
    }

    [Fact]
    public void The_canonical_site_is_configured_as_the_one_the_page_head_writes()
    {
        // render/heads.ts writes every canonical link against its SITE and a post's path as /?view=blog&post=<slug>.
        string heads = File.ReadAllText(Path.Combine(Repo.Root(), "render", "heads.ts"));
        string configured = host.Services.GetRequiredService<CanonicalSite>().Origin;
        Assert.Equal(Site, configured);
        Assert.Contains($"export const SITE = '{configured}';", heads, StringComparison.Ordinal);
        Assert.Contains("path: `/?view=blog&post=${encodeURIComponent(post.slug)}`", heads, StringComparison.Ordinal);
        Assert.Contains($"path: '{BlogSitemap.BlogPath}'", heads, StringComparison.Ordinal);
    }

    [Fact]
    [Covers("GET", "/sitemap-blog.xml", 200)]
    public async Task The_sitemap_is_served_at_the_root_from_the_store()
    {
        var answer = await host.CreateClient().GetAsync("/sitemap-blog.xml");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("application/xml", answer.Content.Headers.ContentType?.MediaType);
        var sitemap = XDocument.Parse(await answer.Content.ReadAsStringAsync());
        string[] locs = sitemap.Descendants(Urls + "loc").Select(loc => loc.Value).ToArray();
        Assert.Equal(Three.Length + 1, locs.Length);
        Assert.Equal(locs.Length, locs.Distinct(StringComparer.Ordinal).Count());
        foreach (var post in Three)
        {
            Assert.Contains(BlogSitemap.PostAddress(Site, post.Slug), locs);
        }
    }
    // #endregion the-sitemap

    // #region the-index
    [Fact]
    public void The_index_puts_the_blog_before_optional_one_line_a_post()
    {
        const string words = "# TheYard, by Steven Stout\n\n> What it is.\n\n## Start here\n\n- [README](https://theyard.stevenstout.biz/api/docs/readme)\n\n## Optional\n\n- [The API reference](https://theyard.stevenstout.biz/api/reference)\n";
        string text = LlmsText.Write(words, Three, Site);

        Assert.Contains(
            "\n\n## Blog\n\n- [The newest & best](https://theyard.stevenstout.biz/?view=blog&post=the-newest-post): One line. And a second.\n",
            text,
            StringComparison.Ordinal);
        Assert.True(text.IndexOf("## Blog", StringComparison.Ordinal) < text.IndexOf("## Optional", StringComparison.Ordinal), "Optional stays last");
        Assert.Contains("post=the-first-post): Two lines about it.\n\n## Optional", text, StringComparison.Ordinal);
        Assert.Equal(Three.Length, Regex.Matches(text, @"view=blog&post=").Count);
        Assert.Equal(words, LlmsText.Write(words, [], Site));
        Assert.EndsWith("## Blog\n\n" + LlmsText.Line(Three[2], Site) + "\n", LlmsText.Write("# Only a title\n", [Three[2]], Site), StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_with_brackets_keeps_the_link_whole()
    {
        string line = LlmsText.Line(Post("a-post", DateTimeOffset.UnixEpoch, "Lists [and] links"), Site);
        Assert.Equal("- [Lists (and) links](https://theyard.stevenstout.biz/?view=blog&post=a-post): Two lines about it.", line);
    }

    [Fact]
    [Covers("GET", "/llms.txt", 200)]
    public async Task The_index_is_served_at_the_root_with_every_post()
    {
        var answer = await host.CreateClient().GetAsync("/llms.txt");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("text/plain", answer.Content.Headers.ContentType?.MediaType);
        string text = await answer.Content.ReadAsStringAsync();
        Assert.StartsWith("# TheYard, by Steven Stout", text, StringComparison.Ordinal);
        Assert.Equal(Three.Length, Regex.Matches(text, @"view=blog&post=").Count);
        Assert.DoesNotContain(Regex.Matches(text, @"\]\((https://[^)]*)\)"), link => Regex.IsMatch(link.Groups[1].Value, @"[?&]r="));
    }
    // #endregion the-index
}

/// <summary>The two crawler files on a host whose store cannot be read: the sitemap says 503, the index stands without its Blog section.</summary>
public sealed class BlogSitemapUnreadTests(BlogUnkeptTests.UnkeptHost host) : IClassFixture<BlogUnkeptTests.UnkeptHost>
{
    [Fact]
    [Covers("GET", "/sitemap-blog.xml", 503)]
    public async Task The_sitemap_says_503_and_the_index_leaves_the_blog_out()
    {
        var client = host.CreateClient();
        await Problem.Is(await client.GetAsync("/sitemap-blog.xml"), HttpStatusCode.ServiceUnavailable);
        string text = await client.GetStringAsync("/llms.txt");
        Assert.StartsWith("# TheYard, by Steven Stout", text, StringComparison.Ordinal);
        Assert.DoesNotContain(LlmsText.BlogHeading, text, StringComparison.Ordinal);
    }
}
