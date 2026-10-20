using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// What the edge may keep for a day because only a deploy changes it, and what the edge card scores
/// (ADR-015 and the edge card's record, the addenda of 20 October): the documents, the diagrams and
/// the share card's picture carry the edge's rule while the browser still asks; the resume, About
/// Steven, the version, the admin reads and the sign-in never do; and every address the hourly
/// sample scores is one the app lets the edge keep, so the card's zero can only mean the edge.
/// </summary>
public class EdgeKeepsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : "";

    [Theory]
    [InlineData("/api/docs/readme")]
    [InlineData("/api/docs/practices")]
    [InlineData("/api/docs/changelog")]
    [InlineData("/api/docs/diagrams/infrastructure")]
    [InlineData("/api/docs/diagrams/erd")]
    public async Task Documents_and_diagrams_are_kept_by_the_edge_for_a_day_and_the_browser_still_asks(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EdgeKeeps.EdgeRule, Header(response, "Netlify-CDN-Cache-Control"));
        Assert.Equal("no-cache", Header(response, "Cache-Control"));
    }

    [Theory]
    [InlineData("/api/docs/resume")]
    [InlineData("/api/docs/author")]
    [InlineData("/api/version")]
    [InlineData("/api/admin/plan")]
    [InlineData("/api/auth/login")]
    public async Task The_counted_documents_the_version_the_admin_reads_and_the_sign_in_are_never_kept(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal("", Header(response, "Netlify-CDN-Cache-Control"));
    }

    [Fact]
    public async Task A_document_asked_for_with_a_cookie_is_never_kept_by_the_edge()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/docs/readme");
        request.Headers.TryAddWithoutValidation("Cookie", "theyard-session=not-a-real-token");

        var response = await _client.SendAsync(request);

        Assert.Equal("", Header(response, "Netlify-CDN-Cache-Control"));
    }

    /// <summary>The share card's picture keeps its name across deploys: a day for the browser and the edge.</summary>
    [Fact]
    public void The_share_picture_is_kept_for_a_day_by_the_browser_and_the_edge()
    {
        var context = Answered("/og.png");

        EdgeKeeps.LetTheEdgeKeep(context);

        Assert.Equal(EdgeKeeps.BrowserRule, EdgeKeeps.BrowserRuleFor(context));
        Assert.Equal(EdgeKeeps.EdgeRule, context.Response.Headers["Netlify-CDN-Cache-Control"].ToString());
        Assert.Null(EdgeKeeps.BrowserRuleFor(Answered("/")));
        Assert.Null(EdgeKeeps.BrowserRuleFor(Answered("/api/version")));
    }

    /// <summary>
    /// Every address the card scores is one the app lets the edge keep. Each runs through the same
    /// two rules the pipeline runs; the ones the test host serves run through the host too.
    /// </summary>
    [Fact]
    public async Task Every_sampled_address_is_one_the_app_lets_the_edge_keep()
    {
        foreach (string path in NetlifyEdge.Sampled)
        {
            var context = Answered(path);
            CatalogueReads.LetTheEdgeKeep(context);
            EdgeKeeps.LetTheEdgeKeep(context);
            Assert.False(string.IsNullOrEmpty(context.Response.Headers["Netlify-CDN-Cache-Control"].ToString()), $"{path} is sampled but the edge may not keep it");

            var response = await _client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.NotEqual("", Header(response, "Netlify-CDN-Cache-Control"));
            }
        }

        Assert.DoesNotContain("/", NetlifyEdge.Sampled);
        Assert.DoesNotContain("/api/version", NetlifyEdge.Sampled);
    }

    [Fact]
    public void The_bundle_file_is_read_from_the_built_page_and_skipped_without_one()
    {
        string page = Path.Combine(Path.GetTempPath(), $"edge-{Guid.NewGuid():N}.html");
        File.WriteAllText(page, "<script type=\"module\" crossorigin src=\"/assets/index-AbC123_x.js\"></script>");
        try
        {
            Assert.Equal("/assets/index-AbC123_x.js", NetlifyEdge.BundleFileOf(page));
            Assert.Null(NetlifyEdge.BundleFileOf(page + ".missing"));
            var edge = new NetlifyEdge("t", "theyard-edge", new Uri("https://theyard.stevenstout.biz"), "/assets/index-AbC123_x.js");
            Assert.Equal("/assets/index-AbC123_x.js", edge.Sample[0]);
            Assert.Equal(NetlifyEdge.Sampled, new NetlifyEdge("t", "theyard-edge", new Uri("https://theyard.stevenstout.biz")).Sample);
        }
        finally
        {
            File.Delete(page);
        }
    }

    private static DefaultHttpContext Answered(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context;
    }
}
