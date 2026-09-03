using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheBlock.Api;

namespace TheBlock.Tests;

/// <summary>
/// Telemetry (ADR-024). The tests run with no connection string and no managed
/// identity, which is the off path, and the off path is the one that must never
/// break a local run: the endpoint still answers, and the card still has
/// something to render.
/// </summary>
public class TelemetryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region telemetry-tests
    [Fact]
    public async Task The_admin_endpoint_answers_even_with_no_telemetry_configured()
    {
        var response = await _client.GetAsync("/api/admin/telemetry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Either shape is correct here: the test host has no identity, so the
        // reader either says it is unconfigured or fails to reach the metadata
        // endpoint. What must never happen is a 500 or a hang.
        Assert.True(json.RootElement.TryGetProperty("configured", out _));
        Assert.True(json.RootElement.TryGetProperty("note", out var note));
        Assert.False(string.IsNullOrWhiteSpace(note.GetString()));
    }

    [Fact]
    public async Task An_unconfigured_reader_says_so_instead_of_reaching_the_network()
    {
        var reader = new TelemetryReader(appId: "", clientId: "");

        Assert.False(reader.Configured);
        // Awaited, but nothing is awaited on the wire: an empty app id
        // short-circuits before any token request, which is what makes this
        // safe to call from a test host with no identity.
        var state = await reader.GetRecentAsync();
        string json = JsonSerializer.Serialize(state);
        Assert.Contains("\"configured\":false", json);
        Assert.Contains("not configured", json);
    }

    [Fact]
    public void A_configured_reader_reports_itself_configured()
    {
        var reader = new TelemetryReader("6ff89351-7fcc-4a41-8238-db65c5903c36", "some-client-id");
        Assert.True(reader.Configured);
    }

    [Fact]
    public async Task A_browser_error_still_records_when_telemetry_is_off()
    {
        // The endpoint logs to Application Insights as well as the ring buffer
        // (ADR-024). With telemetry off the log goes nowhere, and the buffer
        // must still receive it: the durable copy is a bonus, not the path.
        string marker = "telemetry-off probe " + Guid.NewGuid().ToString("N")[..8];
        var posted = await _client.PostAsJsonAsync("/api/errors/client",
            new { message = marker, stack = "at Probe", path = "/?telemetry=off" });

        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);
        Assert.Contains(marker, await _client.GetStringAsync("/api/errors"));
    }
    // #endregion telemetry-tests
}
