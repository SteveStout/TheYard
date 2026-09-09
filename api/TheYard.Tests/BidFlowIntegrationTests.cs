using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

// #region lifecycle
// WebApplicationFactory<Program> boots Program.cs inside the test process with
// an in-memory server: real routing, binding, services and serializer, no port.
// IClassFixture shares that one host across the class.
/// <summary>
/// The bid lifecycle through the real host. Its own fixture class, so the
/// mutable bid state can't leak into the read-only integration tests.
/// </summary>
public class BidFlowIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    // Bidding needs an account now (ADR: Accounts and per-user bids). A field
    // initialiser cannot await, so registering happens in InitializeAsync,
    // which xunit runs once before the first test in the class. That keeps
    // these tests about the bidding rules rather than about registration.
    private HttpClient _client = null!;

    public async Task InitializeAsync() => _client = await Buyers.SignedIn(factory);

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private async Task<JsonDocument> GetAsync(string url) =>
        JsonDocument.Parse(await _client.GetStringAsync(url));

    [Fact]
    public async Task Bid_lifecycle_place_verify_buy_now_and_reset()
    {
        // One clock for the whole test, and it is the server's (ADR: Three
        // readers with no memory of the project, the addendum on the clock).
        // Pick a live vehicle sorted by most bids, because its window ends hours or
        // days out, so it cannot flip to ended mid-test.
        using var live = await GetAsync("/api/vehicles?status=live&sort=most-bids&limit=50");
        // And not one anybody has bought: a sold vehicle takes no bid (ADR:
        // Accounts and per-user bids, the addendum on the second buyer), and on
        // the document store the test containers remember yesterday's runs.
        var target = live.RootElement.GetProperty("vehicles").EnumerateArray()
            .First(vehicle => !vehicle.GetProperty("sold").GetBoolean());
        string id = target.GetProperty("id").GetString()!;
        int min = target.GetProperty("min_next_bid").GetInt32();
        int bidCount = target.GetProperty("bid_count").GetInt32();

        // Place a bid at the minimum.
        var placed = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = min });
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        using var placedJson = JsonDocument.Parse(await placed.Content.ReadAsStringAsync());
        Assert.Equal("accepted", placedJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal(bidCount + 1,
            placedJson.RootElement.GetProperty("bid").GetProperty("bid_count").GetInt32());

        // The single-vehicle read reflects the bid and a raised minimum.
        using var after = await GetAsync($"/api/vehicles/{id}");
        Assert.Equal(min, after.RootElement.GetProperty("current_bid").GetInt32());
        Assert.True(after.RootElement.GetProperty("min_next_bid").GetInt32() > min);

        // Rebidding below the new minimum is rejected server-side.
        var tooLow = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids",
            new { amount = min });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        // #endregion lifecycle

        // Buy Now on a live vehicle that has a price.
        string? buyNowId = null;
        foreach (var vehicle in live.RootElement.GetProperty("vehicles").EnumerateArray())
        {
            if (vehicle.GetProperty("buy_now_price").ValueKind != JsonValueKind.Null
                && !vehicle.GetProperty("sold").GetBoolean())
            {
                buyNowId = vehicle.GetProperty("id").GetString();
                break;
            }
        }
        Assert.NotNull(buyNowId);
        var bought = await _client.PostAsJsonAsync($"/api/vehicles/{buyNowId}/buy-now", new { });
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
        using var ended = await GetAsync("/api/vehicles?status=ended&limit=1");
        string id = ended.RootElement.GetProperty("vehicles")[0].GetProperty("id").GetString()!;

        var response = await _client.PostAsJsonAsync($"/api/vehicles/{id}/bids", new { amount = 1_000_000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // One failure shape for the whole API: the reason is in `detail` (ADR-023).
        Assert.Contains("ended", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bidding_on_an_unknown_vehicle_returns_404()
    {
        var response = await _client.PostAsJsonAsync("/api/vehicles/nope/bids", new { amount = 1_000 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
