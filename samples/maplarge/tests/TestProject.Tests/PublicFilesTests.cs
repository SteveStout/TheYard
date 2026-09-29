using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TestProject.Tests;

/// <summary>
/// The two documents a reviewer opens first, and the author's page: the resume
/// and "The Shed, explained" are served as PDFs from the site's own origin, and
/// the About page sits in the catalogue's first group so the Docs tab shows it.
/// </summary>
public sealed class PublicFilesTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("/resume.pdf")]
    [InlineData("/the-shed-explained.pdf")]
    public async Task A_public_pdf_is_served_as_a_pdf(string path)
    {
        using HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 10_000, $"{path} is {bytes.Length} bytes, which is not a document");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task The_about_page_is_in_the_first_group_and_links_the_resume()
    {
        using HttpClient client = _factory.CreateClient();
        JsonElement catalogue = JsonDocument.Parse(await client.GetStringAsync("/api/docs")).RootElement;
        JsonElement about = catalogue.EnumerateArray().Single(entry => entry.GetProperty("slug").GetString() == "about");
        Assert.Equal("Start here", about.GetProperty("group").GetString());
        Assert.Equal("About Steven", about.GetProperty("title").GetString());
        string markdown = await client.GetStringAsync("/api/docs/about");
        Assert.Contains("(/resume.pdf)", markdown, StringComparison.Ordinal);
        Assert.Contains("(/the-shed-explained.pdf)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)0x2014).ToString(), markdown, StringComparison.Ordinal);
    }
}
