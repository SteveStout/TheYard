using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

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
        var reader = new TelemetryReader(appId: "", clientId: "", enabled: false);

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
        var reader = new TelemetryReader("6ff89351-7fcc-4a41-8238-db65c5903c36", "some-client-id", enabled: true);
        Assert.True(reader.Configured);
    }

    [Fact]
    public void An_app_id_alone_does_not_make_a_reader_configured()
    {
        // The app id has a default at the composition root, so reading it
        // alone made Configured always true, the unconfigured path dead code,
        // and a local request an eight-second wait on Azure's metadata
        // endpoint. The connection string is the evidence; the app id is not.
        var reader = new TelemetryReader("6ff89351-7fcc-4a41-8238-db65c5903c36", "client", enabled: false);

        Assert.False(reader.Configured);
    }

    [Fact]
    public void The_query_avoids_the_three_things_that_made_the_first_one_a_400()
    {
        // 1.0.0.34's query did not parse, and none of the three causes is
        // visible in C#: they are Kusto's rules. Holding them here is cheaper
        // than another deploy to find out (ADR-024, second pass).
        string kql = ReadQuery();

        // `kind` is reserved. `part` is the label column instead.
        Assert.DoesNotContain("kind =", kql);
        Assert.Contains("part = \"requests\"", kql);
        // A literal column belongs in extend; summarize takes aggregations.
        foreach (string line in kql.Split('\n'))
        {
            if (line.Contains("| summarize", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("part =", line);
            }
        }
        // `success` is a string in the classic schema and a bool in the
        // workspace one, so it is compared as text in both.
        Assert.Contains("tostring(success)", kql);
        Assert.DoesNotContain("success == false", kql);
    }

    /// <summary>
    /// The query is private, which is right: it is an implementation detail of
    /// the reader. Reflection is the narrow exception a test earns when the
    /// alternative is making the field public for the test's convenience.
    /// </summary>
    private static string ReadQuery()
    {
        var field = typeof(TelemetryReader).GetField(
            "Query",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        return (string)field!.GetRawConstantValue()!;
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
