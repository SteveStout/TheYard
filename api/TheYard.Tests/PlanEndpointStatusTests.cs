using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

// #region plan-endpoint-status-tests
/// <summary>The one status code the plan slice's route declares, through the real host (ADR: The onion cut into vertical slices, rule 7).</summary>
public sealed class PlanEndpointStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/plan", 200)]
    public async Task The_plan_answers_200_for_any_window_and_names_the_window_it_read()
    {
        var client = factory.CreateClient();

        var week = await client.GetAsync("/api/admin/plan?window=7d");
        Assert.Equal(HttpStatusCode.OK, week.StatusCode);
        using var body = JsonDocument.Parse(await week.Content.ReadAsStringAsync());
        Assert.Equal("7d", body.RootElement.GetProperty("window").GetString());

        var stale = await client.GetAsync("/api/admin/plan?window=sideways");
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        using var day = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal("24h", day.RootElement.GetProperty("window").GetString());
    }
}
// #endregion plan-endpoint-status-tests
