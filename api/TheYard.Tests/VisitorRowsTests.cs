using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The per-visitor rows are off unless a setting turns them on (ADR: Site
/// activity, and the line an address does not cross, sixth addendum). On a
/// host with the key set and the rows left at their default, the two row
/// endpoints are a 404 with the key as without it, and the public report
/// says the rows are not served.
/// </summary>
public class VisitorRowsTests : IClassFixture<VisitorRowsTests.KeyedRowsOffHost>
{
    public sealed class KeyedRowsOffHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-rows-off-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private readonly HttpClient _client;

    public VisitorRowsTests(KeyedRowsOffHost host)
    {
        _client = host.CreateClient();
    }

    // #region rows-off
    [Theory]
    [InlineData("/api/admin/activity/visitors?window=24h")]
    [InlineData("/api/admin/logs/kept?window=24h")]
    public async Task The_rows_are_a_404_even_with_the_key_when_they_are_off(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Admin-Key", KeyedRowsOffHost.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task The_public_report_says_the_rows_are_not_served_and_the_reset_links_still_answer_to_the_key()
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync("/api/admin/activity?window=24h"));
        Assert.False(json.RootElement.GetProperty("visitor_rows").GetBoolean());

        // The operator's other card is not per-visitor data and stays: a 404
        // without the key, and with it an answer about the address given.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email = "nobody-rows-off@example.com" }),
        };
        request.Headers.Add("X-Admin-Key", KeyedRowsOffHost.Key);
        var answered = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, answered.StatusCode);
        Assert.Contains("No account", await answered.Content.ReadAsStringAsync());
    }
    // #endregion rows-off
}
