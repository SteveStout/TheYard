using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The resume's door (ADR: The resume has an address, the addendum on the door): a browser asking /resume for a
/// page gets one band linking to the review route with the link's code kept, and the locked PDF embedded below it;
/// ?pdf=1 and every caller that does not ask for a page get the locked bytes; a value that is not a link code is
/// never carried, and the embed's address carries no code, so a tagged open is counted once.
/// </summary>
public class ResumeDoorPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> AsABrowserAsync(string address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.ParseAdd(BrowserAccept);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_browser_gets_the_door_with_the_route_link_and_the_pdf_inside()
    {
        var response = await AsABrowserAsync("/resume?src=email&r=k7f2qa");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("href=\"/review?r=k7f2qa\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/resume?pdf=1&amp;src=email\"", html, StringComparison.Ordinal);
        Assert.Contains(ResumePage.RouteLink, html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pdf_query_answers_the_locked_bytes_to_a_browser_too()
    {
        var response = await AsABrowserAsync("/resume?pdf=1&src=email");
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        string locked = File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "wwwroot", "docs", "resume.sha256")).Trim();

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(locked, Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    [Fact]
    public async Task A_caller_that_does_not_ask_for_a_page_still_gets_the_pdf()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/resume?src=email");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var response = await _client.SendAsync(request);

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_code_that_is_not_one_is_never_carried()
    {
        var response = await AsABrowserAsync("/resume?src=x&r=%3Cscript%3E");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"/review\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("src=\"/resume?pdf=1&amp;src=other\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_embed_carries_no_code_so_a_tagged_open_is_counted_once()
    {
        string html = ResumePage.Html("email", "k7f2qa");
        int frame = html.IndexOf("<iframe", StringComparison.Ordinal);
        string embed = html[frame..html.IndexOf('>', frame)];

        Assert.DoesNotContain("r=", embed, StringComparison.Ordinal);
        Assert.Single(html.Split("r=k7f2qa").Skip(1));
    }
}
