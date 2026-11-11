using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The funnel by channel's route booted in the host (ADR-109, the addendum on the funnel by channel): public like the
/// funnel's, and off Azure there is no job hunt database to read, so it answers 200 with why and the six channels, in
/// the wire's snake_case, carrying counts only.
/// </summary>
public sealed class ChannelsEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/channels", 200)]
    public async Task Off_Azure_the_route_answers_unread_with_why_in_snake_case_and_counts_only()
    {
        using var response = await factory.CreateClient().GetAsync("/api/admin/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(["available", "note", "channels", "weeks", "source"], root.EnumerateObject().Select(field => field.Name));
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Contains("only Azure holds", root.GetProperty("note").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, root.GetProperty("channels").GetArrayLength());
    }
}
