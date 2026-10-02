using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TestProject.Tests;

/// <summary>
/// Checks the files a visitor is most likely to open first. The resume and "The Shed, explained"
/// must be served from this site as real PDFs: status 200, the PDF content type, more than 10 KB,
/// and the %PDF signature at the start. About Steven is one page, kept on TheYard: the header link
/// goes there and the Docs tab carries no copy of it, so the two sites can never tell two versions
/// of the same story. A broken or empty file here would be the first thing a visitor sees.
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
    public async Task About_Steven_links_to_TheYard_and_the_docs_tab_holds_no_copy()
    {
        string page = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "index.html"));
        Assert.Contains("<a href=\"https://theyard.stevenstout.biz/?doc=author\">About Steven</a>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("doc=about", page, StringComparison.Ordinal);
        using HttpClient client = _factory.CreateClient();
        JsonElement catalogue = JsonDocument.Parse(await client.GetStringAsync("/api/docs")).RootElement;
        Assert.DoesNotContain(catalogue.EnumerateArray(), entry => entry.GetProperty("slug").GetString() == "about");
        Assert.False(File.Exists(Path.Combine(ProjectFolder.Root(), "docs", "ABOUT.md")));
    }
}
