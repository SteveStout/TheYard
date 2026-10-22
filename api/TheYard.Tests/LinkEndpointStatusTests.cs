using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Links;

namespace TheYard.Tests;

// #region link-endpoint-status-tests
/// <summary>
/// Every status the Links slice's routes declare, through the real host (ADR: The onion cut into
/// vertical slices, rule 7), and the one proof that matters: a tagged request from a browser is
/// counted, the same request from a preview fetcher is not, and the card shows the code and never
/// anything else.
/// </summary>
public sealed class LinkEndpointStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private HttpClient Browser()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Chrome);
        return client;
    }

    [Fact]
    [Covers("GET", "/api/go/{key}", 302)]
    [Covers("GET", "/api/go/{key}", 404)]
    public async Task A_pass_through_sends_a_known_key_on_without_the_code_and_refuses_an_unknown_one()
    {
        var client = Browser();
        var github = await client.GetAsync("/api/go/github?r=k7f2qa");
        Assert.Equal(HttpStatusCode.Found, github.StatusCode);
        Assert.Equal("https://github.com/SteveStout/TheYard", github.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync("/api/go/linkedin")).StatusCode);
        await Problem.Is(await client.GetAsync("/api/go/elsewhere?r=k7f2qa"), HttpStatusCode.NotFound);
    }

    [Fact]
    [Covers("GET", "/api/links/open", 204)]
    [Covers("GET", "/api/links/open", 400)]
    public async Task The_page_s_own_report_is_204_for_a_code_on_a_view_it_has_and_400_otherwise()
    {
        var client = Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/links/open?r=k7f2qa&path=/about")).StatusCode);
        await Problem.Is(await client.GetAsync("/api/links/open?r=k7f2qa&path=/admin"), HttpStatusCode.BadRequest);
        await Problem.Is(await client.GetAsync("/api/links/open?r=Steve&path=/"), HttpStatusCode.BadRequest);
        await Problem.Is(await client.GetAsync("/api/links/open"), HttpStatusCode.BadRequest);
    }

    [Fact]
    [Covers("GET", "/api/admin/links", 200)]
    public async Task An_open_from_a_browser_is_counted_under_its_code_and_path_and_a_preview_fetch_is_not()
    {
        var client = Browser();
        // The code is this test's own, so another class's opens never land in its row.
        string code = "t" + Guid.NewGuid().ToString("N")[..5];
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/links/open?r={code}&path=/")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/go/github?r={code}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/resume?src=email&r={code}")).StatusCode);
        // A pass-through the site refuses leaves the row as it was: a guess at a key is not an open.
        await Problem.Is(await client.GetAsync($"/api/go/nope?r={code}"), HttpStatusCode.NotFound);
        var unfurler = factory.CreateClient();
        unfurler.DefaultRequestHeaders.UserAgent.ParseAdd("LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)");
        Assert.Equal(HttpStatusCode.NoContent, (await unfurler.GetAsync($"/api/links/open?r={code}&path=/")).StatusCode);

        // The collector writes off the request path; drain it the way the loop would.
        await factory.Services.GetRequiredService<LinkOpenCollector>().DrainAsync(CancellationToken.None);

        var card = await client.GetAsync("/api/admin/links?window=7d");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        using var json = JsonDocument.Parse(await card.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.Equal("7d", root.GetProperty("window").GetString());
        var row = root.GetProperty("codes").EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == code);
        Assert.Equal(3, row.GetProperty("opens").GetInt32());
        var paths = row.GetProperty("paths");
        Assert.Equal(1, paths.GetProperty("/").GetInt32());
        Assert.Equal(1, paths.GetProperty("/api/go/github").GetInt32());
        Assert.Equal(1, paths.GetProperty("/resume/email").GetInt32());
        Assert.False(paths.TryGetProperty("/api/go/nope", out _));
        // Codes and paths only: nothing in the card's wire names a person, an address or an agent.
        string wire = root.GetRawText();
        Assert.DoesNotContain("Chrome", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("LinkedInBot", wire, StringComparison.Ordinal);
        Assert.Contains("github", root.GetProperty("keys").EnumerateArray().Select(key => key.GetString()));
    }
}
// #endregion link-endpoint-status-tests
