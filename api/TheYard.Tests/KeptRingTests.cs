using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The Admin tab's public lists, kept for a month (ADR: Logs that outlive the
/// container, the addendum on the cards). A ring hands what it takes to the writer and not what it refuses;
/// the writer keeps an entry as the JSON the ring serves, with nothing in the
/// private fields of the spine; the reader answers a card and a window and
/// nothing else; and on a store that keeps them, a ring comes back as written.
/// </summary>
public class KeptRingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;

    public KeptRingTests(WebApplicationFactory<Program> host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    private sealed class ListStore : ILogStore
    {
        public readonly List<LogEvent> Written = [];
        public int Reads;

        public Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new LogAvailability(true, "a list"));

        public Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation)
        {
            Written.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogEvent>>([]);

        public Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogCount>>([]);

        public Task<KeptRingPage> RingAsync(string kind, string site, DateTimeOffset since, int take, CancellationToken cancellation)
        {
            Reads++;
            var held = Written
                .Where(e => e.Kind == kind && e.Store == site && e.At >= since && e.Entry is not null)
                .OrderByDescending(e => e.At)
                .ToList();
            return Task.FromResult(new KeptRingPage(held.Take(take).Select(e => e.Entry!).ToList(), held.Count));
        }
    }

    private static string? RequestOf(string json)
    {
        using var entry = JsonDocument.Parse(json);
        return entry.RootElement.GetProperty("request").GetString();
    }

    private static SqlStatement Statement(string? request, DateTimeOffset? at = null) =>
        new(at ?? DateTimeOffset.UtcNow, "SELECT 1 WHERE @p0 = 1", [new SqlParameterShape("@p0", "Int32", null)], 7, "ok", request);

    // #region rings-and-writer
    [Fact]
    public async Task A_ring_hands_on_what_it_takes_and_the_entry_is_kept_as_the_ring_serves_it()
    {
        var store = new ListStore();
        var collector = new LogCollector(store);
        var writer = new KeptRingWriter(collector, "sql");
        var ring = new SqlRingBuffer(10);
        ring.Kept = statement => writer.Keep(KeptRings.Sql, statement.At, statement);

        ring.Record(Statement("GET /api/vehicles"));
        // The tab watching itself is not in the ring, so it is not kept either.
        ring.Record(Statement("GET /api/health"));
        await collector.DrainAsync(CancellationToken.None);

        LogEvent kept = Assert.Single(store.Written);
        Assert.Equal(KeptRings.Sql, kept.Kind);
        Assert.Equal("sql", kept.Store);
        Assert.Equal(KeptRings.RetentionSeconds, kept.TtlSeconds);
        Assert.True(KeptRings.RetentionSeconds >= 30 * 86_400, "a document must outlive the widest window that reads it");
        // The private fields of the spine stay empty: what is kept is what the public ring serves.
        Assert.Equal(("", "", "", ""), (kept.Visitor, kept.Network, kept.Message, kept.Detail));

        string served = JsonSerializer.Serialize(Assert.Single(ring.Snapshot()), KeptRingWriter.Wire);
        Assert.Equal(served, kept.Entry);
        using var entry = JsonDocument.Parse(kept.Entry!);
        Assert.Equal(7, entry.RootElement.GetProperty("duration_ms").GetInt64());
        Assert.Equal("GET /api/vehicles", entry.RootElement.GetProperty("request").GetString());
    }

    [Fact]
    public async Task Every_public_list_has_a_ring_that_hands_on()
    {
        var store = new ListStore();
        var collector = new LogCollector(store);
        var writer = new KeptRingWriter(collector, "cosmos");
        var errors = new ErrorRingBuffer(5) { Kept = entry => writer.Keep(KeptRings.Errors, entry.At, entry) };
        var lines = new LogRingBuffer(5) { Kept = line => writer.Keep(KeptRings.Log, line.At, line) };
        var operations = new StoreRingBuffer(5) { Kept = operation => writer.Keep(KeptRings.Store, operation.At, operation) };

        errors.Record("/api/vehicles", 500, "InvalidOperationException", ["Frame.One()"]);
        lines.Record(new LogEntry(DateTimeOffset.UtcNow, "Information", "TheYard.Test", "a line", null));
        operations.Record(new StoreOperation(DateTimeOffset.UtcNow, "bids", StoreOperationKind.PointRead, "", [], "one partition", 1, 1.0, 3, "ok", "GET /api/bids", null));
        await collector.DrainAsync(CancellationToken.None);

        List<string> kinds = store.Written.Select(e => e.Kind).ToList();
        List<string> expected = [KeptRings.Errors, KeptRings.Log, KeptRings.Store];
        Assert.Equal(expected, kinds);
        Assert.All(store.Written, e => Assert.Equal("cosmos", e.Store));
        Assert.Equal(4, KeptRings.ByCard.Count);
        // None of the kept kinds is one the keyed log reads, so neither can show the other's documents.
        Assert.Empty(KeptRings.ByCard.Values.Intersect(LogEvent.Kinds));
    }
    // #endregion rings-and-writer

    // #region reader
    [Fact]
    public async Task The_reader_answers_a_card_and_a_window_and_holds_the_answer_for_half_a_minute()
    {
        var store = new ListStore();
        var collector = new LogCollector(store);
        var writer = new KeptRingWriter(collector, "sql");
        var now = DateTimeOffset.UtcNow;
        writer.Keep(KeptRings.Sql, now.AddDays(-2), Statement("GET /api/old", now.AddDays(-2)));
        writer.Keep(KeptRings.Sql, now.AddMinutes(-5), Statement("GET /api/new", now.AddMinutes(-5)));
        await collector.DrainAsync(CancellationToken.None);
        var reader = new KeptRingReader(collector, "sql", "Azure Cosmos DB");

        Assert.Null(await reader.ReadAsync("secrets", "24h", now, CancellationToken.None));
        Assert.Null(await reader.ReadAsync("sql", "1y", now, CancellationToken.None));
        Assert.Null(await reader.ReadAsync(null, "24h", now, CancellationToken.None));

        object? day = await reader.ReadAsync("sql", "24h", now, CancellationToken.None);
        object? again = await reader.ReadAsync("sql", "24h", now.AddSeconds(10), CancellationToken.None);
        object? week = await reader.ReadAsync("sql", "7d", now, CancellationToken.None);
        Assert.Same(day, again);
        Assert.Equal(2, store.Reads);

        using var dayJson = JsonDocument.Parse(JsonSerializer.Serialize(day, KeptRingWriter.Wire));
        using var weekJson = JsonDocument.Parse(JsonSerializer.Serialize(week, KeptRingWriter.Wire));
        Assert.Equal(1, dayJson.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, weekJson.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("GET /api/new", weekJson.RootElement.GetProperty("entries")[0].GetProperty("request").GetString());
        Assert.True(dayJson.RootElement.GetProperty("kept").GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task The_endpoint_is_public_names_its_cards_and_refuses_anything_else()
    {
        foreach (string card in KeptRings.ByCard.Keys)
        {
            var response = await _client.GetAsync($"/api/admin/kept?card={card}&window=7d");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(card, json.RootElement.GetProperty("card").GetString());
            Assert.Equal("7d", json.RootElement.GetProperty("window").GetString());
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("entries").ValueKind);
            Assert.NotEqual("", json.RootElement.GetProperty("kept").GetProperty("note").GetString());
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/admin/kept?card=secrets&window=7d")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/admin/kept?card=sql&window=1y")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/admin/kept")).StatusCode);
    }
    // #endregion reader

    // #region live-ring
    [Fact]
    public async Task On_a_store_that_keeps_them_a_ring_comes_back_as_written_for_its_site_newest_first()
    {
        var store = _host.Services.GetRequiredService<LogCollector>().Store;
        if (!(await store.AvailabilityAsync(CancellationToken.None)).Available)
        {
            return;
        }

        // A site nobody else writes under, on a store that outlives the run.
        string site = "test-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        string older = JsonSerializer.Serialize(Statement("GET /api/older", now.AddSeconds(-2)), KeptRingWriter.Wire);
        string newer = JsonSerializer.Serialize(Statement("GET /api/newer", now.AddSeconds(-1)), KeptRingWriter.Wire);
        await store.AppendAsync(
        [
            KeptRings.Entry(KeptRings.Sql, site, now.AddSeconds(-2), older),
            KeptRings.Entry(KeptRings.Sql, site, now.AddSeconds(-1), newer),
            KeptRings.Entry(KeptRings.Store, site, now, "{\"container\":\"bids\"}"),
            KeptRings.Entry(KeptRings.Sql, site + "-other", now, newer),
        ], CancellationToken.None);

        var page = await store.RingAsync(KeptRings.Sql, site, now.AddMinutes(-1), 10, CancellationToken.None);

        Assert.Equal(2, page.Total);
        List<string?> requests = page.Entries.Select(RequestOf).ToList();
        List<string?> expected = ["GET /api/newer", "GET /api/older"];
        Assert.Equal(expected, requests);
        // The at sign of a parameter's name survives: a kept entry is the ring's entry, not the keyed log's cleaned text.
        Assert.Contains("@p0", page.Entries[0]);

        // And the keyed log, asked for everything, shows none of them.
        var keyed = await store.QueryAsync(new LogQuery(now.AddMinutes(-1), null, null, null, 1_000), CancellationToken.None);
        Assert.DoesNotContain(keyed, e => e.Kind.StartsWith("ring-", StringComparison.Ordinal));
    }
    // #endregion live-ring
}
