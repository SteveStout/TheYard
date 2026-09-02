using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

/// <summary>
/// Error handling (ADR-023): every deliberate failure answers RFC 9457
/// ProblemDetails with the message in `detail`, and a browser-side error
/// reported by the app reaches the same list the Admin tab reads.
/// </summary>
public class ProblemDetailsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region problem-tests
    [Theory]
    [InlineData("/api/vehicles?sort=alphabetical")]
    [InlineData("/api/vehicles?status=sideways")]
    [InlineData("/api/vehicles?status=live&anchor_ms=12345")]
    public async Task Every_rejected_query_answers_problem_details_with_a_readable_detail(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task A_rejected_bid_answers_the_same_shape_as_a_rejected_query()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var page = JsonDocument.Parse(
            await _client.GetStringAsync($"/api/vehicles?status=live&limit=1&anchor_ms={anchor}"));
        string id = page.RootElement.GetProperty("vehicles")[0].GetProperty("id").GetString()!;

        // One dollar can never clear the minimum increment, so this is always rejected.
        var response = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = 1, anchor_ms = anchor });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("bid", json.RootElement.GetProperty("title").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task An_unknown_vehicle_is_still_a_bare_404()
    {
        var response = await _client.GetAsync("/api/vehicles/no-such-vehicle");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_browser_error_is_recorded_where_the_admin_tab_reads_it()
    {
        string marker = "boundary probe " + Guid.NewGuid().ToString("N")[..8];

        var posted = await _client.PostAsJsonAsync("/api/errors/client",
            new { message = marker, stack = "at VehicleCard", path = "/?vehicle=probe" });
        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);

        string errors = await _client.GetStringAsync("/api/errors");
        Assert.Contains(marker, errors);
        // The page the visitor was on is the path, so the list reads like the server's own entries.
        Assert.Contains("/?vehicle=probe", errors);
    }

    [Fact]
    public async Task A_browser_report_without_a_message_is_rejected_in_the_same_shape()
    {
        var response = await _client.PostAsJsonAsync("/api/errors/client", new { message = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
    // #endregion problem-tests
}
