using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

/// <summary>
/// The bid lifecycle through the real host. Its own fixture class, so the
/// mutable bid state can't leak into the read-only integration tests.
/// </summary>
public class BidFlowIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static long Anchor =>
        new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private async Task<JsonDocument> GetAsync(string url) =>
        JsonDocument.Parse(await _client.GetStringAsync(url));

    [Fact]
    public async Task Bid_lifecycle_place_verify_buy_now_and_reset()
    {
        long anchor = Anchor;

        // Pick a live vehicle sorted by most bids — its window ends hours or
        // days out, so it cannot flip to ended mid-test.
        using var live = await GetAsync($"/api/vehicles?status=live&sort=most-bids&limit=50&anchor_ms={anchor}");
        var target = live.RootElement.GetProperty("vehicles")[0];
        string id = target.GetProperty("id").GetString()!;
        int min = target.GetProperty("min_next_bid").GetInt32();
        int bidCount = target.GetProperty("bid_count").GetInt32();

        // Place a bid at the minimum.
        var placed = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = min, anchor_ms = anchor });
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        using var placedJson = JsonDocument.Parse(await placed.Content.ReadAsStringAsync());
        Assert.Equal("accepted", placedJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal(bidCount + 1,
            placedJson.RootElement.GetProperty("bid").GetProperty("bid_count").GetInt32());

        // The single-vehicle read reflects the bid and a raised minimum.
        using var after = await GetAsync($"/api/vehicles/{id}?anchor_ms={anchor}");
        Assert.Equal(min, after.RootElement.GetProperty("current_bid").GetInt32());
        Assert.True(after.RootElement.GetProperty("min_next_bid").GetInt32() > min);

        // Rebidding below the new minimum is rejected server-side.
        var tooLow = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = min, anchor_ms = anchor });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);

        // Buy Now on a live vehicle that has a price.
        string? buyNowId = null;
        foreach (var vehicle in live.RootElement.GetProperty("vehicles").EnumerateArray())
        {
            if (vehicle.GetProperty("buy_now_price").ValueKind != JsonValueKind.Null)
            {
                buyNowId = vehicle.GetProperty("id").GetString();
                break;
            }
        }
        Assert.NotNull(buyNowId);
        var bought = await _client.PostAsJsonAsync($"/api/vehicles/{buyNowId}/buy-now",
            new { anchor_ms = anchor });
        Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        using var boughtJson = JsonDocument.Parse(await bought.Content.ReadAsStringAsync());
        Assert.Equal("won", boughtJson.RootElement.GetProperty("kind").GetString());

        // The bid map lists both, then reset clears everything.
        using var bidMap = await GetAsync("/api/bids");
        Assert.True(bidMap.RootElement.TryGetProperty(id, out _));
        Assert.True(bidMap.RootElement.GetProperty(buyNowId!).GetProperty("won_buy_now").GetBoolean());

        var reset = await _client.DeleteAsync("/api/bids");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var cleared = await GetAsync("/api/bids");
        Assert.Empty(cleared.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Bids_on_ended_auctions_are_rejected_and_leave_no_state()
    {
        long anchor = Anchor;
        using var ended = await GetAsync($"/api/vehicles?status=ended&limit=1&anchor_ms={anchor}");
        string id = ended.RootElement.GetProperty("vehicles")[0].GetProperty("id").GetString()!;

        var response = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = 1_000_000, anchor_ms = anchor });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("ended", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Bidding_on_an_unknown_vehicle_returns_404()
    {
        var response = await _client.PostAsJsonAsync("/api/vehicles/nope/bids",
            new { amount = 1_000, anchor_ms = Anchor });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
