using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheBlock.Api;

namespace TheBlock.Tests;

/// <summary>
/// The three sections the Admin tab grew (ADR: What the database is actually
/// doing): the raw SQL, the raw log, and the timing. The first test in this
/// file is the one that matters, because the page is public.
/// </summary>
public class AdminObservabilityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region redaction
    [Fact]
    public async Task No_parameter_value_reaches_the_sql_endpoint_not_even_an_email_address()
    {
        // A registration is the request that carries the most sensitive
        // parameter this application has, and it touches AspNetUsers on the way
        // through: a normalised-name lookup, a normalised-email lookup, and an
        // insert, all of them parameterised with the address.
        string email = $"leak-canary-{Guid.NewGuid():N}@example.com";
        var registered = await _client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "correct horse battery" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        string body = await _client.GetStringAsync("/api/admin/sql");

        // The statements are there.
        Assert.Contains("AspNetUsers", body, StringComparison.Ordinal);
        // The address is not, in any form. This is the whole point of the
        // section: the type has no field for a parameter value, so there is no
        // rule here that a new column could get past.
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leak-canary", body, StringComparison.OrdinalIgnoreCase);

        // And not through the log section either, which is the door the first
        // version left open. It captures Entity Framework's own command lines,
        // and those render parameters as `@p='?'` only because sensitive data
        // logging is off. This assertion is what holds that switch down: turn it
        // on and this fails here rather than on the live site.
        string logs = await _client.GetStringAsync("/api/admin/logs");
        Assert.Contains("AspNetUsers", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leak-canary", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_statement_describes_its_parameters_without_valuing_them()
    {
        await _client.GetAsync("/api/vehicles?limit=1");
        string body = await _client.GetStringAsync("/api/admin/sql");
        using var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.GetArrayLength() > 0, "the application ran some SQL to answer that");
        foreach (var statement in json.RootElement.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(statement.GetProperty("text").GetString()));
            Assert.True(statement.GetProperty("duration_ms").GetInt64() >= 0);
            foreach (var parameter in statement.GetProperty("parameters").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("type").GetString()));
                // The serialized shape has three fields and none of them is a value.
                var names = parameter.EnumerateObject().Select(property => property.Name).ToArray();
                Assert.Equal(new[] { "name", "size", "type" }, names.OrderBy(name => name, StringComparer.Ordinal).ToArray());
            }
        }
    }
    // #endregion redaction

    [Fact]
    public async Task A_statement_names_the_request_that_caused_it()
    {
        // A sign-in attempt for an account that does not exist, which is the
        // cheapest request in the application that certainly reaches the
        // database: it is one lookup on AspNetUsers and then a refusal.
        //
        // Most GETs here run no SQL at all. The catalogue is read once at
        // startup and held in memory, so /api/vehicles answers without touching
        // the store, and a test that asked one of those to produce a statement
        // would be asserting a coincidence about which other test ran first.
        await _client.PostAsJsonAsync(
            "/api/auth/login", new { email = "nobody@example.com", password = "wrong password" });

        string body = await _client.GetStringAsync("/api/admin/sql");
        using var json = JsonDocument.Parse(body);

        var requests = json.RootElement.EnumerateArray()
            .Select(statement => statement.GetProperty("request").GetString())
            .Where(request => request is not null)
            .ToArray();
        Assert.Contains("POST /api/auth/login", requests);
        // And nothing stronger. Every test in this class shares one server and
        // therefore one ring, xUnit orders them by a hash of their names, and an
        // earlier test's registration puts its own statements in there. An
        // assertion that every entry came from this request would pass today and
        // break when somebody renames a test (the staff review, 2026-09-03).
    }

    [Fact]
    public async Task The_log_section_carries_lines_with_a_level_and_a_category()
    {
        string body = await _client.GetStringAsync("/api/admin/logs");
        using var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.GetArrayLength() > 0, "starting the application writes log lines");
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("level").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("category").GetString()));
        }
    }

    [Fact]
    public async Task The_metrics_section_reports_a_window_and_per_path_timings()
    {
        await _client.GetAsync("/api/facets");
        await _client.GetAsync("/api/facets");
        string body = await _client.GetStringAsync("/api/admin/metrics");
        using var json = JsonDocument.Parse(body);

        var requests = json.RootElement.GetProperty("requests");
        Assert.True(requests.GetProperty("window").GetInt32() >= 2);
        Assert.True(requests.GetProperty("p95_ms").GetInt64() >= requests.GetProperty("p50_ms").GetInt64());

        var facets = requests.GetProperty("by_path").EnumerateArray()
            .Single(timing => timing.GetProperty("path").GetString() == "/api/facets");
        Assert.True(facets.GetProperty("count").GetInt32() >= 2);
        Assert.True(facets.GetProperty("max_ms").GetInt64() >= facets.GetProperty("p50_ms").GetInt64());

        Assert.True(json.RootElement.GetProperty("sql").GetProperty("window").GetInt32() >= 0);
    }

    // #region percentiles
    [Theory]
    [InlineData(new long[] { 5 }, 50, 5)]
    [InlineData(new long[] { 5 }, 95, 5)]
    [InlineData(new long[] { 1, 2 }, 50, 1)]
    [InlineData(new long[] { 1, 2 }, 95, 2)]
    [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 50, 5)]
    [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 95, 10)]
    [InlineData(new long[] { 10, 1, 5 }, 50, 5)]
    public void The_percentile_is_the_nearest_rank(long[] values, int percentile, long expected) =>
        Assert.Equal(expected, Percentiles.Of(values, percentile));

    [Fact]
    public void An_empty_sample_has_no_percentile_rather_than_an_exception() =>
        Assert.Equal(0, Percentiles.Of([], 95));

    [Fact]
    public void Paths_are_ordered_busiest_first()
    {
        var now = DateTimeOffset.UtcNow;
        RequestEntry[] entries =
        [
            new(now, "GET", "/quiet", 200, 3),
            new(now, "GET", "/busy", 200, 10),
            new(now, "GET", "/busy", 200, 20),
            new(now, "GET", "/busy", 500, 30),
        ];

        var timings = Percentiles.ByPath(entries);

        Assert.Equal("/busy", timings[0].Path);
        Assert.Equal(3, timings[0].Count);
        Assert.Equal(30, timings[0].MaxMs);
        Assert.Equal("/quiet", timings[1].Path);
    }
    // #endregion percentiles

    [Fact]
    public void A_ring_keeps_the_newest_and_drops_the_oldest()
    {
        var ring = new RequestRingBuffer(2);
        var now = DateTimeOffset.UtcNow;
        ring.Record(new RequestEntry(now, "GET", "/first", 200, 1));
        ring.Record(new RequestEntry(now, "GET", "/second", 200, 1));
        ring.Record(new RequestEntry(now, "GET", "/third", 200, 1));

        var snapshot = ring.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("/third", snapshot[0].Path);
        Assert.Equal("/second", snapshot[1].Path);
    }
}
