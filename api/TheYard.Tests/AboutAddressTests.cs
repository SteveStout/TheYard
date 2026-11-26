using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Application.Features.Links;

namespace TheYard.Tests;

/// <summary>
/// The About link a message carries (ADR: Tagged links, the addendum on the About link): /about?r=&lt;code&gt;, already in
/// inboxes, answers 302 to the author page with the code kept, the same shape as the /review door, so the page it lands
/// on reports the open once under /about. Without a code, or with a value that is not one, /about is the page for
/// crawlers, as before.
/// </summary>
public sealed class AboutAddressTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task About_with_a_code_answers_a_redirect_to_the_author_page_with_the_code_kept()
    {
        using var response = await _client.GetAsync("/about?r=k7f2qa");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/?doc=author&r=k7f2qa", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/about")]
    [InlineData("/about?r=NOT-A-CODE")]
    [InlineData("/about?r=")]
    public async Task About_without_a_code_is_the_page_for_crawlers(string asked)
    {
        using var response = await _client.GetAsync(asked);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("takes work from requirements through production", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_door_is_not_an_open_and_the_page_it_lands_on_reports_one_under_about()
    {
        const string chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0";
        var at = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(TaggedRequests.OpenOf("GET", "/about", "k7f2qa", null, chrome, at, "sql", 302));
        Assert.Equal(new LinkOpen(at, "k7f2qa", "/about", "sql"), TaggedRequests.OpenOf("GET", TaggedRequests.PageReport, "k7f2qa", "/about", chrome, at, "sql"));
    }
}
