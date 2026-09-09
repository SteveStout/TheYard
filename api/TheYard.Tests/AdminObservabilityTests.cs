using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The three sections the Admin tab grew (ADR: What the database is actually
/// doing): the raw SQL, the raw log, and the timing. The first test in this
/// file is the one that matters, because the page is public.
/// </summary>
public class AdminObservabilityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region what-the-store-ran
    /// <summary>
    /// Whichever store this container is on, what it ran. The SQL section on a
    /// relational container, the operations section on a document one, so the
    /// same test holds the no-values rule against both stores rather than
    /// against the one it was written for (ADR: What the store is actually
    /// doing).
    /// </summary>
    private async Task<(bool Cosmos, string Body)> WhatTheStoreRan()
    {
        string store = await _client.GetStringAsync("/api/admin/store");
        using var json = JsonDocument.Parse(store);
        bool cosmos = json.RootElement.GetProperty("store").GetString() == "Azure Cosmos DB";
        return cosmos
            ? (true, json.RootElement.GetProperty("operations").GetRawText())
            : (false, await _client.GetStringAsync("/api/admin/sql"));
    }
    // #endregion what-the-store-ran

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

        var (cosmos, body) = await WhatTheStoreRan();

        // The statements are there: the users table on one store, the users
        // container on the other, and on the document store the claim document
        // whose id IS the address is the one most worth checking.
        Assert.Contains(cosmos ? "users" : "AspNetUsers", body, StringComparison.Ordinal);
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
        if (!cosmos)
        {
            Assert.Contains("AspNetUsers", logs, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leak-canary", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_statement_describes_its_parameters_without_valuing_them()
    {
        await _client.GetAsync("/api/vehicles?limit=1");
        var (_, body) = await WhatTheStoreRan();
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

        var (_, body) = await WhatTheStoreRan();
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

    /// <summary>
    /// The console log gives the document store one line per operation, the
    /// shape Entity Framework gives every statement, so the Admin tab's log
    /// card shows both stores' traffic (ADR: What the store is actually doing,
    /// addendum). On a container with a document store a request served by it
    /// leaves a line under the store's own category; on SQLite alone the SQL
    /// lines are still there and nothing pretends otherwise.
    /// </summary>
    [Fact]
    public async Task The_log_gives_the_document_store_a_line_per_operation_beside_the_sql_ones()
    {
        var stores = await _client.GetFromJsonAsync<JsonElement>("/api/stores");
        bool document = stores.GetProperty("stores").EnumerateArray()
            .Any(store => store.GetProperty("key").GetString() == "cosmos");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/facets");
        request.Headers.Add(Backends.HeaderName, document ? "cosmos" : "sql");
        (await _client.SendAsync(request)).EnsureSuccessStatusCode();
        // A sign-in attempt reaches the store on either side: Identity looks
        // the address up, which is a statement on one store and a point read
        // on the other.
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody-" + Guid.NewGuid().ToString("N") + "@example.com", password = "wrong horse" }),
        };
        login.Headers.Add(Backends.HeaderName, document ? "cosmos" : "sql");
        await _client.SendAsync(login);

        string body = await _client.GetStringAsync("/api/admin/logs");
        using var json = JsonDocument.Parse(body);
        var entries = json.RootElement.EnumerateArray().ToList();

        Assert.Contains(entries, entry => entry.GetProperty("category").GetString() == "Microsoft.EntityFrameworkCore.Database.Command"
            || entry.GetProperty("category").GetString()!.StartsWith("TheYard.", StringComparison.Ordinal));
        if (document)
        {
            var lines = entries
                .Where(entry => entry.GetProperty("category").GetString() == "TheYard.Infrastructure.Cosmos.CosmosStore")
                .ToList();
            Assert.NotEmpty(lines);
            foreach (var line in lines)
            {
                string message = line.GetProperty("message").GetString()!;
                Assert.StartsWith("Executed Cosmos DB ", message);
                Assert.Contains(" RU, ", message);
                Assert.Contains(" ms, ", message);
            }
            // Never a value: the address that was looked up is not on the page.
            Assert.DoesNotContain("@example.com", body);
        }
    }

    /// <summary>
    /// Every store a container runs answers a window of its own on the same
    /// rows, which is what the Timing card's two store lines and the
    /// comparison card draw from (ADR: Backends, side by side, the addendum on
    /// parity): the relational backend a SQL block, the document backend a
    /// store_metrics block with the request units and the cross-partition
    /// count beside the percentiles. The ship gate runs this on both shapes.
    /// </summary>
    [Fact]
    public async Task Every_store_the_container_runs_answers_a_window_of_its_own()
    {
        await _client.GetAsync("/api/facets");
        string body = await _client.GetStringAsync("/api/admin/metrics");
        using var json = JsonDocument.Parse(body);

        var backends = json.RootElement.GetProperty("backends").EnumerateArray().ToList();
        Assert.NotEmpty(backends);
        foreach (var backend in backends)
        {
            if (!backend.GetProperty("ready").GetBoolean())
            {
                continue;
            }

            bool relational = backend.GetProperty("sql").ValueKind != JsonValueKind.Null;
            bool document = backend.GetProperty("store_metrics").ValueKind != JsonValueKind.Null;
            Assert.True(relational || document, $"{backend.GetProperty("key")} answers no window at all");
            if (document)
            {
                var store = backend.GetProperty("store_metrics");
                foreach (string field in new[] { "window", "p50_ms", "p95_ms", "max_ms", "ru_total", "cross_partition" })
                {
                    Assert.True(store.TryGetProperty(field, out _), $"store_metrics lacks {field}");
                }
            }
            if (relational)
            {
                var sql = backend.GetProperty("sql");
                foreach (string field in new[] { "window", "p50_ms", "p95_ms", "max_ms" })
                {
                    Assert.True(sql.TryGetProperty(field, out _), $"sql lacks {field}");
                }
            }
        }

        // On a container running both, both kinds are present at once.
        if (backends.Count > 1)
        {
            Assert.Contains(backends, backend => backend.GetProperty("store_metrics").ValueKind != JsonValueKind.Null);
            Assert.Contains(backends, backend => backend.GetProperty("sql").ValueKind != JsonValueKind.Null);
        }
    }

    // #region status
    [Fact]
    public async Task A_request_that_throws_is_counted_as_the_status_its_caller_got()
    {
        // The self-test endpoint throws on purpose and answers 500. It is the
        // one request in the application guaranteed to take the exception path.
        var failed = await _client.GetAsync("/api/admin/selftest/exception");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        string body = await _client.GetStringAsync("/api/admin/metrics");
        using var json = JsonDocument.Parse(body);

        var statuses = json.RootElement.GetProperty("by_status").EnumerateArray()
            .ToDictionary(
                entry => entry.GetProperty("status").GetInt32(),
                entry => entry.GetProperty("count").GetInt32());

        // This is the assertion that pins the middleware's position. Below the
        // exception handler, the finally reads the status before the handler
        // writes one, and this request is filed as a 200: the first version of
        // the timing section did exactly that, so the endpoint that exists to
        // prove the failure path works was reported as a success.
        Assert.True(
            statuses.TryGetValue(500, out int failures) && failures >= 1,
            "a request that threw should be counted as a 500, saw: "
            + string.Join(", ", statuses.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    [Fact]
    public async Task The_endpoints_the_admin_tab_reads_stay_out_of_its_own_numbers()
    {
        await _client.GetAsync("/api/facets");
        await _client.GetAsync("/api/health");
        await _client.GetAsync("/api/admin/logs");

        string body = await _client.GetStringAsync("/api/admin/metrics");
        using var json = JsonDocument.Parse(body);
        var paths = json.RootElement.GetProperty("requests").GetProperty("by_path").EnumerateArray()
            .Select(timing => timing.GetProperty("path").GetString())
            .ToArray();

        Assert.Contains("/api/facets", paths);
        Assert.DoesNotContain("/api/health", paths);
        Assert.DoesNotContain("/api/admin/logs", paths);
        Assert.DoesNotContain("/api/admin/metrics", paths);
        // But the deliberate failure is not an observability read, and the
        // timing section is exactly where it should show up.
        Assert.DoesNotContain(paths, path => path == "/api/admin/sql");
    }
    // #endregion status

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
