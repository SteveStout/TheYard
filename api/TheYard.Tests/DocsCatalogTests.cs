using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The documents catalog (ADR-017): every slug serves markdown with its live
/// blocks expanded, an unknown slug is a 404, and the catalog and the sidebar's
/// record name exactly the same slugs.
/// </summary>
public class DocsCatalogTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Every_catalog_slug_serves_markdown_with_no_live_fence_left()
    {
        foreach (string slug in DocsCatalog.Files.Keys)
        {
            var response = await _client.GetAsync($"/api/docs/{slug}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(body), $"{slug} served an empty document");
            Assert.DoesNotContain("```live", body);
        }
    }

    [Theory]
    [InlineData("/api/docs/nope")]
    [InlineData("/api/docs/adr-999")]
    [InlineData("/api/docs/ADR-001-front-door-origin.md")]
    public async Task An_unknown_slug_is_a_404_not_a_file_read(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void The_catalog_and_the_sidebar_record_name_the_same_slugs_and_every_file_exists()
    {
        string root = RepoRoot();
        string menu = File.ReadAllText(Path.Combine(root, "src", "components", "DocsMenu.tsx"));
        var inMenu = Regex.Matches(menu, @"url: '/api/docs/([a-z0-9-]+)'")
            .Select(m => m.Groups[1].Value)
            .Where(slug => slug is not "bicep") // the Bicep file has its own route: it is not markdown
            .ToHashSet(StringComparer.Ordinal);
        var inCatalog = DocsCatalog.Files.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(inMenu.SetEquals(inCatalog),
            "sidebar only: [" + string.Join(", ", inMenu.Except(inCatalog)) +
            "]; catalog only: [" + string.Join(", ", inCatalog.Except(inMenu)) + "]");
        foreach (string file in DocsCatalog.Files.Values)
        {
            Assert.True(File.Exists(Path.Combine(root, file)), $"{file} is missing from the checkout");
        }
    }

    /// <summary>
    /// The sidebar's Diagrams section lists every drawing the server can open
    /// on a page, and nothing else (ADR: Every diagram opens on its own page,
    /// the addendum on the section). The same shape as the slugs above: the
    /// server's catalog is the authority, the sidebar's list is read as
    /// source, and the two are held equal so a drawing cannot gain a page
    /// without a row or a row without a page.
    /// </summary>
    // #region docs-images
    /// <summary>
    /// A document's pictures come from this site (1.0.3.5): the raw-host address
    /// every markdown file carries for GitHub's sake is pointed at
    /// /api/docs/images here, and a PNG with an SVG source beside it is served as
    /// the SVG, which is what took the README from 1.4 MB to a tenth of that.
    /// </summary>
    [Fact]
    public void A_documents_pictures_are_pointed_at_this_site_and_a_drawn_png_at_its_svg()
    {
        string root = Repo.Root();
        string markdown = "![The app](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg) and "
            + "![The drawing](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/infrastructure.png) and "
            + "![A shot](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-phone.png) and "
            + "[a file](https://raw.githubusercontent.com/SteveStout/TheYard/main/README.md) and "
            + "![elsewhere](https://example.com/docs/images/app-home.jpg)";

        string served = DocImages.Rewrite(markdown, root);

        // Each carries its own size after a hash, read from the file, which the request never sends.
        Assert.Contains("](/api/docs/images/app-home.jpg#1280x800)", served);
        // infrastructure.svg is beside the PNG; toggle-phone has no SVG and stays a PNG.
        Assert.Matches(@"\]\(/api/docs/images/infrastructure\.svg#\d+x\d+\)", served);
        Assert.Matches(@"\]\(/api/docs/images/toggle-phone\.png#\d+x\d+\)", served);
        // A raw-host address that is not a picture, and a picture on another host, are left alone.
        Assert.Contains("(https://raw.githubusercontent.com/SteveStout/TheYard/main/README.md)", served);
        Assert.Contains("(https://example.com/docs/images/app-home.jpg)", served);
    }

    /// <summary>
    /// A picture's size comes from its own header, so the page can hold the room
    /// for it before it arrives (the operator's look: the README's lead picture
    /// pushed a paragraph off the screen after the first paint).
    /// </summary>
    [Fact]
    public void A_pictures_size_is_read_from_its_own_header()
    {
        string images = Path.Combine(Repo.Root(), "docs", "images");

        Assert.Equal((1280, 800), DocImages.SizeOf(Path.Combine(images, "app-home.jpg")));
        Assert.Equal((1400, 1370), DocImages.SizeOf(Path.Combine(images, "infrastructure.svg")));
        Assert.Null(DocImages.SizeOf(Path.Combine(images, "nothing-by-this-name.png")));
    }

    [Fact]
    public async Task Every_picture_a_served_document_names_is_in_the_repository_and_served()
    {
        string root = Repo.Root();
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in DocsCatalog.Files.Values.Distinct())
        {
            string served = DocImages.Rewrite(File.ReadAllText(Path.Combine(root, file)), root);
            foreach (Match match in Regex.Matches(served, @"/api/docs/images/([A-Za-z0-9-]+\.(?:png|jpg|jpeg|svg|webp))"))
            {
                named.Add(match.Groups[1].Value);
            }

            Assert.DoesNotContain(DocImages.RawHost, served);
        }

        Assert.True(named.Count >= 20, $"only {named.Count} pictures are named across the served documents");
        var missing = named.Where(name => !File.Exists(Path.Combine(root, "docs", "images", name))).ToList();
        Assert.True(missing.Count == 0, "these pictures are named by a served document and are not in docs/images: " + string.Join(", ", missing));

        // One of each kind answers with its type and a day's cache.
        foreach (string name in new[] { "app-home.jpg", "infrastructure.svg", "toggle-phone.png" })
        {
            var response = await _client.GetAsync("/api/docs/images/" + name);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(DocImages.ContentType(name), response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
        }
    }

    [Theory]
    [InlineData("/api/docs/images/nope.png")]
    [InlineData("/api/docs/images/..%2F..%2FREADME.md")]
    [InlineData("/api/docs/images/infrastructure.svg.bak")]
    [InlineData("/api/docs/images/README.md")]
    public async Task A_picture_that_is_not_there_or_not_a_picture_name_is_a_404_not_a_file_read(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    // #endregion docs-images

    [Fact]
    public void The_sidebar_lists_every_diagram_page_and_no_other()
    {
        string root = RepoRoot();
        string menu = File.ReadAllText(Path.Combine(root, "src", "components", "DocsMenu.tsx"));
        var inMenu = Regex.Matches(menu, @"href: '/api/docs/diagrams/([a-z0-9-]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var inCatalog = DocsCatalog.Diagrams.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(inMenu.Count > 0, "the sidebar should list the diagram pages");
        Assert.True(inMenu.SetEquals(inCatalog),
            "sidebar only: [" + string.Join(", ", inMenu.Except(inCatalog)) +
            "]; catalog only: [" + string.Join(", ", inCatalog.Except(inMenu)) + "]");
    }

    /// <summary>The folder README.md and src/ sit in, found by walking up from the test binaries.</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }
}
