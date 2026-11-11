using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// Time to first touch's route booted in the host (ADR-109, the addendum on time to first touch): public like the
/// funnel's, and off Azure there is no workspace to read, so it answers 200 with seven unread days and why, in the
/// wire's snake_case, and nothing on the wire names a company, a title or a person.
/// </summary>
public sealed class PounceEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/pounce", 200)]
    public async Task Off_Azure_the_route_answers_seven_unread_days_with_why_in_snake_case()
    {
        using var response = await factory.CreateClient().GetAsync("/api/admin/pounce");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Contains("not read here", root.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal(60, root.GetProperty("target_min").GetInt32());
        Assert.True(root.GetProperty("read_ms").GetInt64() > 0);
        var days = root.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(7, days.Count);
        Assert.All(days, day =>
        {
            Assert.Equal(
                ["day", "read", "caught", "touched", "median_min", "worst_min", "waiting", "oldest_wait_min", "judgement"],
                day.EnumerateObject().Select(field => field.Name));
            Assert.False(day.GetProperty("read").GetBoolean());
        });
    }

    [Fact]
    public async Task The_route_is_open_like_the_funnels_with_no_key_asked()
    {
        using var funnel = await factory.CreateClient().GetAsync("/api/admin/funnel");
        using var pounce = await factory.CreateClient().GetAsync("/api/admin/pounce");

        Assert.Equal(funnel.StatusCode, pounce.StatusCode);
    }
}
