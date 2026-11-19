using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The review route's short path through the real host (ADR: The review route gives a reason before it asks, the
/// addendum on the short path): the chart's route answers the very lines the served Quality page draws its first chart
/// from, and both routes answer 200 with their shapes. The host's catalogue is the dataset in memory (RULE 79).
/// </summary>
public sealed class ReviewShortPathRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task The_route_s_readings_are_the_lines_the_served_Quality_page_draws_its_first_chart_from()
    {
        var client = factory.CreateClient();
        string served = await client.GetStringAsync("/api/docs/quality");
        // The first trend fence on the page: its head line, then one reading a line, up to the fence's end.
        int start = served.IndexOf("```quality-trend\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "the Quality page has a trend fence");
        string[] fence = served[(start + "```quality-trend\n".Length)..].Split("\n```", 2)[0].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.StartsWith("Async methods with no CancellationToken", fence[0], StringComparison.Ordinal);

        using var trend = JsonDocument.Parse(await client.GetStringAsync("/api/review/trend"));
        var readings = trend.RootElement.GetProperty("readings").EnumerateArray().Select(line => line.GetString()).ToArray();
        Assert.Equal(fence[1..], readings);
    }

    [Fact]
    [Covers("GET", "/api/review/trend", 200)]
    [Covers("GET", "/api/review/lot", 200)]
    public async Task The_short_path_s_two_reads_answer_through_the_host()
    {
        var client = factory.CreateClient();
        var trend = await client.GetAsync("/api/review/trend");
        Assert.Equal(HttpStatusCode.OK, trend.StatusCode);
        using var trendJson = JsonDocument.Parse(await trend.Content.ReadAsStringAsync());
        Assert.Equal("async_without_cancellation", trendJson.RootElement.GetProperty("number").GetString());
        Assert.True(trendJson.RootElement.GetProperty("readings").GetArrayLength() > 0);

        var lot = await client.GetAsync("/api/review/lot");
        Assert.Equal(HttpStatusCode.OK, lot.StatusCode);
        using var lotJson = JsonDocument.Parse(await lot.Content.ReadAsStringAsync());
        var root = lotJson.RootElement;
        Assert.True(root.GetProperty("total").GetInt32() > 0);
        // Live or not, the answer says which and carries the vehicle only when there is one.
        Assert.Equal(root.GetProperty("live").GetBoolean(), root.GetProperty("vehicle_id").ValueKind == JsonValueKind.String);
    }
}
