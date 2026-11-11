using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// Every answer says how long this process spent on it in a Server-Timing header, by name and milliseconds only, so a
/// visitor's browser and the roll probe can tell the app's own time from the wire's (ADR: A rendering service beside the API).
/// </summary>
public sealed class ServerTimingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/version")]
    [InlineData("/about")]
    [InlineData("/api/no-such-route")]
    public async Task Every_answer_names_the_app_s_time_and_nothing_else(string path)
    {
        using var response = await factory.CreateClient().GetAsync(path);

        string timing = Assert.Single(response.Headers.GetValues("Server-Timing"));
        Assert.Matches(@"^app;dur=\d+(\.\d)?$", timing);
    }
}
