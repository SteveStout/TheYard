using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

// #region bid-endpoint-status-tests
/// <summary>
/// Every status code the Bids slice's routes declare, one case each through the real host (ADR:
/// The onion cut into vertical slices, rule 7). The handlers' answers are held by BidHandlerTests;
/// this holds that each answer reaches the wire as the status the browser relies on.
/// </summary>
[Collection(SharedBidStore.Name)]
public sealed class BidEndpointStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Unknown = "no-vehicle-has-this-id";

    /// <summary>
    /// A live, unsold vehicle read fresh, with its minimum next bid, and one with a buy-now price.
    /// Taken from the far end of the cheapest fifty, never the most-bid one the account tests take
    /// (AuthTests.ALiveVehicle): on the document store every test host shares one database, and two
    /// classes bidding on one vehicle at once refuse each other's minimum, as 1.0.3.191's first gate
    /// on Cosmos DB found.
    /// </summary>
    private static async Task<(string Id, int Min, string? BuyNowId)> LiveVehiclesAsync(HttpClient client)
    {
        using var live = JsonDocument.Parse(await client.GetStringAsync("/api/vehicles?status=live&sort=price-asc&limit=50"));
        string id = "";
        int min = 0;
        string? buyNowId = null;
        foreach (var candidate in live.RootElement.GetProperty("vehicles").EnumerateArray().Reverse().Where(vehicle => !vehicle.GetProperty("sold").GetBoolean()))
        {
            string candidateId = candidate.GetProperty("id").GetString()!;
            using var fresh = JsonDocument.Parse(await client.GetStringAsync($"/api/vehicles/{candidateId}"));
            if (fresh.RootElement.GetProperty("sold").GetBoolean())
            {
                continue;
            }
            if (buyNowId is null && fresh.RootElement.GetProperty("buy_now_price").ValueKind != JsonValueKind.Null)
            {
                buyNowId = candidateId;
            }
            else if (id.Length == 0)
            {
                id = candidateId;
                min = fresh.RootElement.GetProperty("min_next_bid").GetInt32();
            }
            if (id.Length > 0 && buyNowId is not null)
            {
                break;
            }
        }
        Assert.NotEmpty(id);
        return (id, min, buyNowId);
    }

    [Fact]
    [Covers("POST", "/api/vehicles/{id}/bids", 200)]
    [Covers("POST", "/api/vehicles/{id}/bids", 400)]
    [Covers("POST", "/api/vehicles/{id}/bids", 401)]
    [Covers("POST", "/api/vehicles/{id}/bids", 404)]
    [Covers("GET", "/api/bids", 200)]
    [Covers("DELETE", "/api/bids", 204)]
    public async Task A_bid_answers_200_400_401_and_404_and_the_badges_and_the_start_over_answer_with_it()
    {
        var anonymous = factory.CreateClient();
        var client = await Buyers.SignedIn(factory);
        var (id, min, _) = await LiveVehiclesAsync(client);

        await Problem.Is(await anonymous.PostAsJsonAsync($"/api/vehicles/{id}/bids", new { amount = min }), HttpStatusCode.Unauthorized);
        await Problem.Is(await client.PostAsJsonAsync($"/api/vehicles/{Unknown}/bids", new { amount = min }), HttpStatusCode.NotFound);
        await Problem.Is(await client.PostAsJsonAsync($"/api/vehicles/{id}/bids", new { amount = 1 }), HttpStatusCode.BadRequest);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/vehicles/{id}/bids", new { amount = min })).StatusCode);
            var mine = await client.GetAsync("/api/bids");
            Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
            Assert.Contains(id, await mine.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/bids")).StatusCode);
        }
    }

    [Fact]
    [Covers("POST", "/api/vehicles/{id}/buy-now", 200)]
    [Covers("POST", "/api/vehicles/{id}/buy-now", 400)]
    [Covers("POST", "/api/vehicles/{id}/buy-now", 401)]
    [Covers("POST", "/api/vehicles/{id}/buy-now", 404)]
    public async Task Buy_now_answers_200_400_401_and_404()
    {
        var anonymous = factory.CreateClient();
        var client = await Buyers.SignedIn(factory);
        var (id, _, buyNowId) = await LiveVehiclesAsync(client);
        Assert.NotNull(buyNowId);

        await Problem.Is(await anonymous.PostAsJsonAsync($"/api/vehicles/{buyNowId}/buy-now", new { }), HttpStatusCode.Unauthorized);
        await Problem.Is(await client.PostAsJsonAsync($"/api/vehicles/{Unknown}/buy-now", new { }), HttpStatusCode.NotFound);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/vehicles/{buyNowId}/buy-now", new { })).StatusCode);
            // Bought, so a second purchase is refused by the rules.
            await Problem.Is(await client.PostAsJsonAsync($"/api/vehicles/{buyNowId}/buy-now", new { }), HttpStatusCode.BadRequest);
        }
        finally
        {
            // The buyer's start-over undoes the sale, so the shared stores carry nothing into the next run.
            await client.DeleteAsync("/api/bids");
        }
        Assert.NotEmpty(id);
    }

    [Fact]
    [Covers("GET", "/api/bids/history", 200)]
    [Covers("GET", "/api/bids/history", 401)]
    [Covers("POST", "/api/market/tick", 200)]
    [Covers("POST", "/api/market/tick", 401)]
    [Covers("DELETE", "/api/bids", 401)]
    public async Task The_history_the_rooms_round_and_the_start_over_need_a_session()
    {
        var anonymous = factory.CreateClient();
        var client = await Buyers.SignedIn(factory);

        await Problem.Is(await anonymous.GetAsync("/api/bids/history"), HttpStatusCode.Unauthorized);
        await Problem.Is(await anonymous.PostAsJsonAsync("/api/market/tick", new { }), HttpStatusCode.Unauthorized);
        await Problem.Is(await anonymous.DeleteAsync("/api/bids"), HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bids/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/market/tick", new { })).StatusCode);
    }
}
// #endregion bid-endpoint-status-tests
