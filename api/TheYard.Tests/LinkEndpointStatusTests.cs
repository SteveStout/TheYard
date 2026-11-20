using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Links;
using TheYard.Application.Features.Links;

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

    /// <summary>
    /// The card's row for <paramref name="code"/> once it holds <paramref name="opens"/> opens. The collector's own loop may
    /// have taken an open off the channel and still be writing it when a drain here finds the channel empty, so the card
    /// is read again until the row is there, for up to ten seconds (CodeRabbit on 1.0.5.51).
    /// </summary>
    private async Task<JsonDocument> CardWithRow(HttpClient client, string code, int opens)
    {
        var collector = factory.Services.GetRequiredService<LinkOpenCollector>();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await collector.DrainAsync(CancellationToken.None);
            var json = JsonDocument.Parse(await (await client.GetAsync("/api/admin/links?window=7d")).Content.ReadAsStringAsync());
            bool there = json.RootElement.GetProperty("codes").EnumerateArray()
                .Any(entry => entry.GetProperty("code").GetString() == code && entry.GetProperty("opens").GetInt32() >= opens);
            if (there || DateTime.UtcNow > until)
            {
                return json;
            }
            json.Dispose();
            await Task.Delay(100);
        }
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
    public async Task The_email_key_alone_may_be_a_mailto_and_its_open_is_kept_under_its_path()
    {
        // The "Where next" footer's contact line (ladder v7, R7): an email link through a recorded redirect, so the
        // site counts the open the way it counts GitHub's and LinkedIn's, and no drawn page holds the address.
        var client = Browser();
        string code = "e" + Guid.NewGuid().ToString("N")[..5];
        var email = await client.GetAsync($"/api/go/email?r={code}");
        Assert.Equal(HttpStatusCode.Found, email.StatusCode);
        Assert.Equal("mailto:stevenstout11@gmail.com?subject=TheYard", email.Headers.Location!.ToString());
        using var json = await CardWithRow(client, code, 1);
        var row = json.RootElement.GetProperty("codes").EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == code);
        Assert.Equal(1, row.GetProperty("paths").GetProperty("/api/go/email").GetInt32());
    }

    [Fact]
    public async Task The_book_key_is_a_mailto_of_its_own_with_the_request_for_a_time_and_its_open_is_kept_apart_from_email()
    {
        // The review route's "Book 20 minutes": the visitor's own email app opens with the message ready, and the
        // click is counted under its own path, apart from the contact line's "email".
        var client = Browser();
        string code = "b" + Guid.NewGuid().ToString("N")[..5];
        var book = await client.GetAsync($"/api/go/book?r={code}");
        Assert.Equal(HttpStatusCode.Found, book.StatusCode);
        Assert.Equal(BookAddress, book.Headers.Location!.OriginalString);
        using var json = await CardWithRow(client, code, 1);
        var row = json.RootElement.GetProperty("codes").EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == code);
        Assert.Equal(1, row.GetProperty("paths").GetProperty("/api/go/book").GetInt32());
        Assert.False(row.GetProperty("paths").TryGetProperty("/api/go/email", out _));
    }

    [Fact]
    [Covers("GET", "/api/links/book", 200)]
    [Covers("GET", "/api/links/book", 404)]
    public async Task The_booking_sheet_reads_the_same_address_book_redirects_to_in_three_parts_and_nothing_when_none_is_set()
    {
        // The review route's booking sheet, asked only when it opens: the address, subject and body of the very mailto
        // /api/go/book sends in its Location header, so the read shows nothing that redirect does not.
        var client = Browser();
        var book = await client.GetAsync("/api/links/book");
        Assert.Equal(HttpStatusCode.OK, book.StatusCode);
        using var json = JsonDocument.Parse(await book.Content.ReadAsStringAsync());
        Assert.Equal("stevenstout11@gmail.com", json.RootElement.GetProperty("to").GetString());
        Assert.Equal("20 minutes with Steven Stout", json.RootElement.GetProperty("subject").GetString());
        Assert.StartsWith("Hi Steven,\r\n\r\nI looked at The Yard", json.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
        var expected = BookMessageHandler.Parts(BookAddress);
        Assert.Equal(expected.Body, json.RootElement.GetProperty("body").GetString());

        using var none = factory.WithWebHostBuilder(host => host.UseSetting(ConfiguredLinkTargets.Book, ""));
        await Problem.Is(await none.CreateClient().GetAsync("/api/links/book"), HttpStatusCode.NotFound);
    }

    /// <summary>The booking address, exactly as the site's settings hold it: the subject and the body, encoded.</summary>
    private const string BookAddress =
        "mailto:stevenstout11@gmail.com?subject=20%20minutes%20with%20Steven%20Stout&body=Hi%20Steven%2C%0D%0A%0D%0AI%20looked%20at%20The%20Yard%20and%20would%20like%2020%20minutes%20to%20talk.%0D%0A%0D%0ATimes%20that%20work%20for%20me%2C%20with%20my%20time%20zone%3A%0D%0A1.%0D%0A2.%0D%0A%0D%0ARole%20and%20company%3A%0D%0AWhat%20has%20to%20work%3A%0D%0AWhere%20is%20it%20today%3A%0D%0AWhat%20does%20success%20look%20like%3A%0D%0A";

    [Fact]
    [Covers("GET", "/api/links/open", 204)]
    [Covers("GET", "/api/links/open", 400)]
    public async Task The_page_s_own_report_is_204_for_a_code_on_a_view_it_has_and_400_otherwise()
    {
        var client = Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/links/open?r=k7f2qa&path=/about")).StatusCode);
        // The blog and the Quality page report their own opens since ladder v7b, so a footer click lands under them.
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/links/open?r=k7f2qa&path=/blog")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/links/open?r=k7f2qa&path=/quality")).StatusCode);
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

        // The collector writes off the request path; drain it the way the loop would, until the row is there.
        using var json = await CardWithRow(client, code, 3);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/links?window=7d")).StatusCode);
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
