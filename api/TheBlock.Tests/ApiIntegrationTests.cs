using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

/// <summary>
/// End-to-end through the real host: DI wiring, the synthetic 100k dataset,
/// filtering/sorting/paging parameters, endpoint shapes, and static images.
/// </summary>
public class ApiIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int ExpectedTotal = 100_000;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonDocument> GetAsync(string url) =>
        JsonDocument.Parse(await _client.GetStringAsync(url));

    [Fact]
    public async Task Default_page_is_100_of_the_full_synthetic_dataset_in_snake_case()
    {
        var response = await _client.GetAsync("/api/vehicles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"body_style\"", body);
        Assert.DoesNotContain("\"bodyStyle\"", body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(ExpectedTotal, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("vehicles").GetArrayLength());
    }

    [Fact]
    public async Task Default_order_is_auction_time_with_live_vehicles_first()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        // Capture "now" BEFORE the request: anything the server saw as live
        // cannot have been ended at this earlier instant, however close to
        // its boundary it is (at 100k scale the soonest end is seconds away).
        long nowBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var json = await GetAsync($"/api/vehicles?anchor_ms={anchor}");

        // With ~43% of 100k live, the whole first page is live auctions
        // ordered by soonest end.
        long previousEnd = long.MinValue;
        foreach (var vehicle in json.RootElement.GetProperty("vehicles").EnumerateArray())
        {
            string id = vehicle.GetProperty("id").GetString()!;
            var window = TheBlock.Domain.AuctionSchedule.Window(id, anchor);
            Assert.NotEqual(
                TheBlock.Domain.AuctionStatus.Ended,
                TheBlock.Domain.AuctionSchedule.Status(window, nowBefore));
            Assert.True(window.EndsAtMs >= previousEnd, "live page must be ordered by soonest end");
            previousEnd = window.EndsAtMs;
        }
    }

    [Fact]
    public async Task Limit_parameter_controls_the_page_size()
    {
        using var json = await GetAsync("/api/vehicles?limit=5");
        Assert.Equal(5, json.RootElement.GetProperty("vehicles").GetArrayLength());
        Assert.Equal(ExpectedTotal, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Offset_parameter_pages_through_the_results()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var first = await GetAsync($"/api/vehicles?sort=price-asc&limit=3&anchor_ms={anchor}");
        using var second = await GetAsync($"/api/vehicles?sort=price-asc&limit=3&offset=3&anchor_ms={anchor}");

        var firstIds = first.RootElement.GetProperty("vehicles").EnumerateArray()
            .Select(v => v.GetProperty("id").GetString()).ToList();
        var secondIds = second.RootElement.GetProperty("vehicles").EnumerateArray()
            .Select(v => v.GetProperty("id").GetString()).ToList();

        Assert.Equal(3, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds));
    }

    [Fact]
    public async Task Vehicles_carry_server_derived_auction_facts()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var json = await GetAsync($"/api/vehicles?limit=1&anchor_ms={anchor}");
        var vehicle = json.RootElement.GetProperty("vehicles")[0];

        long startsAt = vehicle.GetProperty("auction_starts_at").GetInt64();
        long endsAt = vehicle.GetProperty("auction_ends_at").GetInt64();
        Assert.True(endsAt > startsAt);
        Assert.Contains(vehicle.GetProperty("auction_status").GetString(),
            new[] { "live", "upcoming", "ended" });
        Assert.True(vehicle.GetProperty("min_next_bid").GetInt32() > 0);
    }

    [Fact]
    public async Task Sort_by_price_ascending_orders_the_page()
    {
        using var json = await GetAsync("/api/vehicles?sort=price-asc&limit=50");

        int previous = int.MinValue;
        foreach (var vehicle in json.RootElement.GetProperty("vehicles").EnumerateArray())
        {
            int price = vehicle.GetProperty("current_bid").ValueKind == JsonValueKind.Null
                ? vehicle.GetProperty("starting_bid").GetInt32()
                : vehicle.GetProperty("current_bid").GetInt32();
            Assert.True(price >= previous, "prices must not decrease");
            previous = price;
        }
    }

    [Fact]
    public async Task Unknown_sort_returns_400()
    {
        var response = await _client.GetAsync("/api/vehicles?sort=alphabetical");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Vehicle_images_point_at_the_api_and_are_served_by_it()
    {
        using var json = await GetAsync("/api/vehicles?limit=1");

        var images = json.RootElement.GetProperty("vehicles")[0].GetProperty("images")
            .EnumerateArray()
            .Select(image => image.GetString()!)
            .ToList();
        Assert.NotEmpty(images);
        Assert.All(images, url => Assert.StartsWith("/api/images/", url));

        var image = await _client.GetAsync(images[0]);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Single_vehicle_lookup_round_trips_the_id()
    {
        using var list = await GetAsync("/api/vehicles?limit=1");
        string id = list.RootElement.GetProperty("vehicles")[0].GetProperty("id").GetString()!;

        using var single = await GetAsync($"/api/vehicles/{id}");
        Assert.Equal(id, single.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_vehicle_id_returns_404()
    {
        var response = await _client.GetAsync("/api/vehicles/not-a-real-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Filters_by_make_via_query_parameter()
    {
        using var json = await GetAsync("/api/vehicles?make=Ford&limit=50");

        var vehicles = json.RootElement.GetProperty("vehicles").EnumerateArray().ToList();
        Assert.NotEmpty(vehicles);
        Assert.All(vehicles, v => Assert.Equal("Ford", v.GetProperty("make").GetString()));
        Assert.InRange(json.RootElement.GetProperty("total").GetInt32(), 1, ExpectedTotal - 1);
    }

    [Fact]
    public async Task Combines_multiple_filters()
    {
        using var json = await GetAsync(
            "/api/vehicles?body_style=SUV&price_max=30000&min_condition=3&limit=50");

        foreach (var vehicle in json.RootElement.GetProperty("vehicles").EnumerateArray())
        {
            Assert.Equal("SUV", vehicle.GetProperty("body_style").GetString());
            Assert.True(vehicle.GetProperty("condition_grade").GetDouble() >= 3);
            int price = vehicle.GetProperty("current_bid").ValueKind == JsonValueKind.Null
                ? vehicle.GetProperty("starting_bid").GetInt32()
                : vehicle.GetProperty("current_bid").GetInt32();
            Assert.True(price <= 30000);
        }
    }

    [Fact]
    public async Task Text_search_narrows_the_results()
    {
        using var json = await GetAsync("/api/vehicles?q=bronco&limit=50");

        Assert.InRange(json.RootElement.GetProperty("total").GetInt32(), 1, ExpectedTotal - 1);
        Assert.All(json.RootElement.GetProperty("vehicles").EnumerateArray(),
            vehicle => Assert.Equal("Bronco", vehicle.GetProperty("model").GetString()));
    }

    [Fact]
    public async Task Status_filter_returns_a_proper_subset()
    {
        using var json = await GetAsync("/api/vehicles?status=live");
        Assert.InRange(json.RootElement.GetProperty("total").GetInt32(), 1, ExpectedTotal - 1);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("9")]
    [InlineData("live,ended")]
    public async Task Invalid_status_values_return_400(string status)
    {
        var response = await _client.GetAsync($"/api/vehicles?status={status}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_filter_honours_the_clients_midnight_anchor()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var json = await GetAsync($"/api/vehicles?status=live&anchor_ms={anchor}");
        Assert.InRange(json.RootElement.GetProperty("total").GetInt32(), 1, ExpectedTotal - 1);
    }

    [Fact]
    public async Task Implausible_anchor_returns_400()
    {
        var response = await _client.GetAsync("/api/vehicles?status=live&anchor_ms=12345");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decimal_price_bounds_are_accepted_with_integer_semantics()
    {
        using var json = await GetAsync("/api/vehicles?price_max=30000.5&limit=1");
        Assert.InRange(json.RootElement.GetProperty("total").GetInt32(), 1, ExpectedTotal - 1);
    }

    [Fact]
    public async Task Unmatched_filters_return_an_empty_page_not_an_error()
    {
        using var json = await GetAsync("/api/vehicles?make=DeLorean");
        Assert.Equal(0, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("vehicles").GetArrayLength());
    }

    [Fact]
    public async Task About_documents_are_served()
    {
        var readme = await _client.GetAsync("/api/docs/readme");
        Assert.Equal(HttpStatusCode.OK, readme.StatusCode);
        Assert.Contains("The Block", await readme.Content.ReadAsStringAsync());

        var dataflow = await _client.GetAsync("/api/docs/dataflow");
        Assert.Equal(HttpStatusCode.OK, dataflow.StatusCode);
        Assert.Contains("Data Flow", await dataflow.Content.ReadAsStringAsync());

        var projects = await _client.GetAsync("/api/docs/projects");
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
        Assert.Contains("TheBlock.Data", await projects.Content.ReadAsStringAsync());

        var resume = await _client.GetAsync("/api/docs/resume");
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        Assert.Equal("application/pdf", resume.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Facets_cover_the_filterable_fields()
    {
        using var json = await GetAsync("/api/facets");

        Assert.Contains("Ford",
            json.RootElement.GetProperty("makes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(5, json.RootElement.GetProperty("body_styles").GetArrayLength());
        Assert.Equal(3, json.RootElement.GetProperty("title_statuses").GetArrayLength());
    }
}
