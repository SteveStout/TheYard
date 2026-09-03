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
