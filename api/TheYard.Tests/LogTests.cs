using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// Logs that outlive the container (ADR: Logs that outlive the container).
/// The cleaning without a store, the collector and the provider against a
/// store made of a list, and then the keyed endpoint on a real host, where
/// the rule is asserted on the wire: after an address was posted as a browser
/// error and written into a path, the response carries no at sign.
/// </summary>
public class LogTests
{
    // #region cleaning
    [Fact]
    public void Text_is_bounded_and_an_at_sign_cannot_survive_it()
    {
        Assert.Equal("", LogText.Clean(null, 10));
        Assert.Equal("someone%40example.com", LogText.Clean("someone@example.com", 100));
        Assert.Equal("abc", LogText.Clean("abcdef", 3));
        Assert.DoesNotContain("@", LogText.Clean(new string('@', 5_000), LogText.DetailLength));
    }

    [Fact]
    public void A_request_event_keeps_the_spine_and_nothing_a_person_could_be_named_by()
    {
        var e = LogEvents.Request(
            DateTimeOffset.UtcNow, "GET", "/api/vehicles/someone@example.com?who=me@x.y", 404, 12, "sql", "0123456789abcdef0123456789abcdef", "203.0.113.x", "trace");
        Assert.Equal(LogEvent.RequestKind, e.Kind);
        Assert.Equal("Warning", e.Level);
        Assert.DoesNotContain("@", e.Path);
        Assert.Equal("/api/vehicles/someone%40example.com?who=me%40x.y", e.Path);
        Assert.Equal("203.0.113.x", e.Network);
        Assert.Equal(404, e.Status);
    }

    [Fact]
    public void A_logged_exception_becomes_an_error_event_with_its_type_and_a_cleaned_bounded_detail()
    {
        Exception thrown = null!;
        try
        {
            throw new InvalidOperationException("account someone@example.com already exists " + new string('x', 5_000));
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var e = LogEvents.Line(DateTimeOffset.UtcNow, LogLevel.Error, "TheYard.Test", "it broke for someone@example.com", thrown, "cosmos", "/api/auth/register", "trace");
        Assert.Equal(LogEvent.ErrorKind, e.Kind);
        Assert.StartsWith("InvalidOperationException: account someone%40example.com", e.Detail);
        Assert.True(e.Detail.Length <= LogText.DetailLength);
        Assert.DoesNotContain("@", e.Detail);
        Assert.DoesNotContain("@", e.Message);

        var warning = LogEvents.Line(DateTimeOffset.UtcNow, LogLevel.Warning, "TheYard.Test", "slow", null, "sql", "/", "");
        Assert.Equal(LogEvent.AppKind, warning.Kind);
        Assert.Equal("", warning.Detail);
    }
    // #endregion cleaning

    // #region collector
    private sealed class ListStore : ILogStore
    {
        public readonly List<LogEvent> Written = [];
        public int Batches;

        public Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new LogAvailability(true, "a list"));

        public Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation)
        {
            Batches++;
            Written.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogEvent>>(Written.Where(e => e.At >= query.Since).ToList());

        public Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<LogCount>>(Written.GroupBy(e => e.Kind).Select(g => new LogCount(g.Key, g.Count())).ToList());
    }

    [Fact]
    public async Task The_collector_writes_what_it_was_offered_in_one_batch_and_counts_it()
    {
        var store = new ListStore();
        var collector = new LogCollector(store);
        for (int i = 0; i < 3; i++)
        {
            collector.Offer(LogEvents.Request(DateTimeOffset.UtcNow, "GET", "/", 200, 1, "sql", "", "", ""));
        }

        await collector.DrainAsync(CancellationToken.None);
        await collector.DrainAsync(CancellationToken.None);

        Assert.Equal(1, store.Batches);
        Assert.Equal(3, store.Written.Count);
        Assert.Equal((3L, 3L, 0L), (collector.Counters.Offered, collector.Counters.Written, collector.Counters.FailedBatches));
        Assert.NotNull(collector.Counters.LastWrite);
    }

    [Fact]
    public async Task The_provider_takes_warnings_and_errors_from_this_application_and_nothing_else()
    {
        var store = new ListStore();
        var collector = new LogCollector(store);
        using var provider = new CollectorLoggerProvider(collector, () => ("cosmos", "/somewhere", "t1"));

        var ours = provider.CreateLogger("TheYard.Something");
        ours.LogInformation("not kept: information is the request log's job");
        ours.LogWarning("kept as app: {Who}", "someone@example.com");
        ours.LogError(new InvalidOperationException("boom"), "kept as error");
        provider.CreateLogger("Microsoft.Hosting.Lifetime").LogWarning("not kept: not our category");
        provider.CreateLogger(CollectorLoggerProvider.UnhandledCategory).LogError(new Exception("unhandled"), "kept: the framework's unhandled exception line");

        await collector.DrainAsync(CancellationToken.None);

        Assert.Equal(3, store.Written.Count);
        Assert.Equal(new[] { LogEvent.AppKind, LogEvent.ErrorKind, LogEvent.ErrorKind }, store.Written.Select(e => e.Kind).ToList());
        Assert.All(store.Written, e => Assert.Equal("cosmos", e.Store));
        Assert.All(store.Written, e => Assert.Equal("/somewhere", e.Path));
        Assert.DoesNotContain("@", store.Written[0].Message);
        Assert.StartsWith("InvalidOperationException: boom", store.Written[1].Detail);
    }
    // #endregion collector
}

/// <summary>The keyed endpoint on a real host, with the collector drained by hand.</summary>
public class LogEndpointTests : IClassFixture<LogEndpointTests.KeyedHost>
{
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-log-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private readonly KeyedHost _host;
    private readonly HttpClient _client;

    public LogEndpointTests(KeyedHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    // #region endpoints
    [Fact]
    public async Task The_kept_log_is_a_404_without_the_key_and_carries_no_at_sign_with_it()
    {
        // An address in a path, and an address posted as a browser error,
        // which is logged on arrival and so reaches the collector.
        await _client.GetAsync("/api/vehicles/someone@example.com");
        await _client.PostAsJsonAsync("/api/errors/client", new { message = "it broke for someone@example.com", stack = "at x@y", path = "/room/me@example.com" });
        var collector = _host.Services.GetRequiredService<LogCollector>();
        await collector.DrainAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/logs/kept")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/logs/kept?key=wrong")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/logs/kept?window=24h&path=someone");
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("24h", root.GetProperty("window").GetString());
        Assert.Equal(3, root.GetProperty("counts").GetArrayLength());
        Assert.True(collector.Counters.Offered >= 2);
        bool kept = root.GetProperty("kept").GetProperty("available").GetBoolean();
        var events = root.GetProperty("events").EnumerateArray().ToList();
        if (!kept)
        {
            // A host with no document store keeps nothing and says so; the
            // store's own behaviour is held on the gate's Cosmos DB pass.
            Assert.Empty(events);
            Assert.NotEqual("", root.GetProperty("kept").GetProperty("reason").GetString());
            return;
        }

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "request" && e.GetProperty("path").GetString()!.Contains("someone%40example.com"));
        foreach (var e in events)
        {
            Assert.Contains(e.GetProperty("kind").GetString()!, LogEvent.Kinds);
            Assert.False(e.TryGetProperty("email", out _));
            Assert.False(e.TryGetProperty("address", out _));
        }
    }

    [Fact]
    public async Task A_kind_or_a_window_that_is_not_one_of_the_names_is_refused()
    {
        using var kind = new HttpRequestMessage(HttpMethod.Get, "/api/admin/logs/kept?kind=secrets");
        kind.Headers.Add("X-Admin-Key", KeyedHost.Key);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(kind)).StatusCode);

        using var window = new HttpRequestMessage(HttpMethod.Get, "/api/admin/logs/kept?window=1y");
        window.Headers.Add("X-Admin-Key", KeyedHost.Key);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(window)).StatusCode);
    }

    [Fact]
    public async Task On_a_store_that_keeps_them_a_filtered_query_returns_only_that_kind_newest_first()
    {
        var store = _host.Services.GetRequiredService<LogCollector>().Store;
        if (!(await store.AvailabilityAsync(CancellationToken.None)).Available)
        {
            return;
        }

        // A path nobody else writes, on a store that outlives the run.
        string marker = "/log-test/" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await store.AppendAsync(
        [
            LogEvents.Request(now.AddSeconds(-2), "GET", marker, 200, 5, "sql", "", "x", ""),
            LogEvents.Request(now.AddSeconds(-1), "GET", marker, 500, 7, "cosmos", "", "x", ""),
            LogEvents.Line(now, LogLevel.Warning, "TheYard.Test", "warned", null, "sql", marker, ""),
        ], CancellationToken.None);

        var requests = await store.QueryAsync(new LogQuery(now.AddMinutes(-5), LogEvent.RequestKind, null, marker, 50), CancellationToken.None);
        Assert.Equal(2, requests.Count);
        Assert.Equal(new[] { 500, 200 }, requests.Select(e => e.Status).ToList());

        var failed = await store.QueryAsync(new LogQuery(now.AddMinutes(-5), null, 500, marker, 50), CancellationToken.None);
        var only = Assert.Single(failed);
        Assert.Equal("cosmos", only.Store);

        var counts = await store.CountAsync(now.AddMinutes(-5), CancellationToken.None);
        Assert.Contains(counts, c => c.Kind == LogEvent.RequestKind && c.Count >= 2);
    }
    // #endregion endpoints
}
