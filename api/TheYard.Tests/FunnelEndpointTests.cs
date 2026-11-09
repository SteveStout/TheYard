using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The funnel's route booted in the host (ADR-109, the addendum on the funnel): off Azure there is no workspace to
/// read, so it answers 200 with the card's unread report and why, in the wire's snake_case.
/// </summary>
public sealed class FunnelEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/funnel", 200)]
    public async Task Off_Azure_the_route_answers_unread_with_why_in_snake_case()
    {
        using var response = await factory.CreateClient().GetAsync("/api/admin/funnel?window=7d");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("unread", body.RootElement.GetProperty("tone").GetString());
        Assert.Equal(6, body.RootElement.GetProperty("counts").GetArrayLength());
        Assert.Equal(2, body.RootElement.GetProperty("week_target").GetInt32());
    }
}
