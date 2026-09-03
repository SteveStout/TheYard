using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

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

    // #region error-eviction
    [Fact]
    public async Task A_flood_of_browser_reports_cannot_push_out_a_server_error()
    {
        // The endpoint that throws on purpose, so there is a real server error
        // in the buffer to try to lose.
        var failed = await _client.GetAsync("/api/admin/selftest/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        // More browser reports than the whole buffer used to hold. Anybody can
        // send these: the endpoint is anonymous on purpose, so that a crash in
        // the page reaches the same place a crash in the server does.
        for (int i = 0; i < 60; i++)
        {
            var posted = await _client.PostAsJsonAsync(
                "/api/errors/client", new { message = $"flood {i}", path = "/?flood=1" });
            Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);
        }

        string body = await _client.GetStringAsync("/api/errors");

        // Still one list, and the server error is still in it. Sharing fifty
        // slots, sixty anonymous posts erased every real error on the page an
        // operator would open during an outage.
        Assert.Contains("selftest", body, StringComparison.Ordinal);
        Assert.Contains("browser: flood 59", body, StringComparison.Ordinal);
    }
    // #endregion error-eviction

    // #region readiness-and-health
    [Fact]
    public async Task The_database_is_the_one_check_that_does_not_withhold_the_container_from_service()
    {
        // Readiness answers "send traffic here". Health answers "how is it".
        // A container whose database is gone still serves the catalogue, the
        // filters, the photos and the bidding out of files, and the only thing
        // it has lost is bids outliving the process, so it is degraded and
        // entirely able to serve.
        //
        // This is not theoretical. The 1.0.0.51 deploy failed on
        // `curl -fsS /readyz` because the database check gated readiness, while
        // the site it was checking was answering with 100,000 vehicles.
        var response = await _client.GetAsync("/api/health");
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        var database = checks.Single(check => check.GetProperty("name").GetString() == "database");
        Assert.False(database.GetProperty("gates_readiness").GetBoolean());

        // And every other check does gate it. A check that reports a missing
        // dataset file while the container claims to be ready would be worse
        // than no check.
        foreach (var check in checks.Where(check => check.GetProperty("name").GetString() != "database"))
        {
            Assert.True(
                check.GetProperty("gates_readiness").GetBoolean(),
                check.GetProperty("name").GetString() + " should gate readiness");
        }
    }
    // #endregion readiness-and-health

    [Fact]
    public async Task Health_reports_status_checks_and_build_in_snake_case()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("checks").GetArrayLength() >= 3);
        foreach (var check in json.RootElement.GetProperty("checks").EnumerateArray())
        {
            Assert.True(check.GetProperty("duration_ms").GetInt64() >= 0, "every check reports how long it took");
        }
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
