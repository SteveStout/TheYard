using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

/// <summary>
/// The observability surfaces behind the Admin tab (ADR-010): liveness,
/// readiness, structured health, and the error buffer.
/// </summary>
public class AdminEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Healthz_answers_ok()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readyz_is_ready_when_the_dataset_and_docs_are_present()
    {
        var response = await _client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_reports_status_checks_and_build_in_snake_case()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("checks").GetArrayLength() >= 3);
        Assert.True(json.RootElement.TryGetProperty("uptime_seconds", out _));
        Assert.True(json.RootElement.TryGetProperty("version", out _));
        Assert.True(json.RootElement.TryGetProperty("commit", out _));
    }

    [Fact]
    public async Task Errors_endpoint_returns_a_json_list()
    {
        var response = await _client.GetAsync("/api/errors");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }
}
