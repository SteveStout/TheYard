using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TestProject.Tests;

/// <summary>
/// Checks the files a visitor is most likely to open first. The resume and "The Shed, explained"
/// must be served from this site as real PDFs: status 200, the PDF content type, more than 10 KB,
/// and the %PDF signature at the start. The About page must sit in the "Start here" group of the
/// document list so the Docs tab shows it first, must link both PDFs, and must hold no em dash.
/// A broken or empty file here would be the first thing a visitor sees.
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
