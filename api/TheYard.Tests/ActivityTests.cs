using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// Site activity (ADR: Site activity, and the line an address does not cross).
/// The arithmetic without a store, the token and the network without a
/// request, and then the two endpoints on a real host with the collector
/// drained by hand, so the tests do not wait on its clock.
///
/// <para>The rule the feature is built on is asserted on the wire and not
/// in prose: after requests that carried an address in the path and in the
/// query string, neither response contains an at sign.</para>
/// </summary>
public class ActivityTests
{
    private static ActivityHit Hit(string store, string at, string visitor = "v1", string path = "/api/vehicles", bool bot = false) =>
        new(DateTimeOffset.Parse(at, null, System.Globalization.DateTimeStyles.AssumeUniversal), visitor, "203.0.113.x", path, store, bot);

    // #region folding
    [Fact]
    public void Hits_fold_into_one_delta_per_store_and_hour()
    {
        var hours = ActivityFolding.Hours(
        [
            Hit("sql", "2026-09-13T10:05:00Z"),
            Hit("sql", "2026-09-13T10:59:00Z", bot: true),
            Hit("sql", "2026-09-13T11:00:00Z"),
            Hit("cosmos", "2026-09-13T10:30:00Z"),
        ]);

        Assert.Equal(3, hours.Count);
        var sqlTen = Assert.Single(hours, delta => delta.Store == "sql" && delta.Hour.Hour == 10);
        Assert.Equal(2, sqlTen.Requests);
        Assert.Equal(1, sqlTen.Bots);
        Assert.Equal(0, sqlTen.Hour.Minute);
        Assert.Equal(TimeSpan.Zero, sqlTen.Hour.Offset);
    }

    [Fact]
    public void Hits_fold_into_one_delta_per_store_day_and_visitor_with_first_and_last_seen()
    {
        var visitors = ActivityFolding.Visitors(
        [
            Hit("sql", "2026-09-13T10:05:00Z", "a"),
            Hit("sql", "2026-09-13T23:59:00Z", "a"),
            Hit("sql", "2026-09-14T00:01:00Z", "a"),
            Hit("sql", "2026-09-13T12:00:00Z", "b", bot: true),
        ]);

        Assert.Equal(3, visitors.Count);
        var aOnTheThirteenth = Assert.Single(visitors, delta => delta.Visitor == "a" && delta.Day == "2026-09-13");
        Assert.Equal(2, aOnTheThirteenth.Requests);
        Assert.Equal("2026-09-13T10:05:00", aOnTheThirteenth.First.UtcDateTime.ToString("s"));
        Assert.Equal("2026-09-13T23:59:00", aOnTheThirteenth.Last.UtcDateTime.ToString("s"));
        Assert.Equal(1, Assert.Single(visitors, delta => delta.Visitor == "b").Bots);
    }

    [Fact]
    public void A_delta_keeps_the_top_paths_only_and_a_merge_keeps_twenty()
    {
        var hits = Enumerable.Range(0, 30).Select(i => Hit("sql", "2026-09-13T10:00:00Z", path: $"/p{i:00}")).ToList();
        hits.Add(Hit("sql", "2026-09-13T10:00:00Z", path: "/p00"));

        var delta = Assert.Single(ActivityFolding.Hours(hits));
        Assert.Equal(ActivityFolding.PathsKept, delta.Paths.Count);
        Assert.Equal("/p00", delta.Paths[0].Key);
        Assert.Equal(2, delta.Paths[0].Value);

        var stored = Enumerable.Range(0, 25).ToDictionary(i => $"/s{i:00}", i => 100 - i, StringComparer.Ordinal);
        var merged = ActivityFolding.Merge(stored, delta.Paths);
        Assert.Equal(ActivityFolding.PathsStored, merged.Count);
        Assert.Equal(100, merged["/s00"]);
    }
    // #endregion folding

    // #region token
    [Fact]
    public void The_token_is_thirty_two_hex_characters_the_same_within_a_day_and_different_across_days_and_keys()
    {
        var tokens = new VisitorTokens("a signing key");
        var morning = DateTimeOffset.Parse("2026-09-13T08:00:00Z");
        var evening = DateTimeOffset.Parse("2026-09-13T22:00:00Z");
        var tomorrow = DateTimeOffset.Parse("2026-09-14T08:00:00Z");

        string token = tokens.TokenFor("203.0.113.7", morning);
        Assert.Matches("^[0-9a-f]{32}$", token);
        Assert.Equal(token, tokens.TokenFor("203.0.113.7", evening));
        Assert.NotEqual(token, tokens.TokenFor("203.0.113.7", tomorrow));
        Assert.NotEqual(token, tokens.TokenFor("203.0.113.8", morning));
        Assert.NotEqual(token, new VisitorTokens("another key").TokenFor("203.0.113.7", morning));
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.x")]
    [InlineData("10.0.0.1", "10.0.0.x")]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8:85a3:x")]
    [InlineData("::1", "::1:x")]
    [InlineData("", "x")]
    [InlineData(null, "x")]
    [InlineData("not an address", "x")]
    [InlineData("999.1.1.1", "x")]
    public void The_network_is_the_address_cut_short_and_never_the_address(string? address, string expected) =>
        Assert.Equal(expected, VisitorTokens.NetworkOf(address));
    // #endregion token

    // #region hits
    [Theory]
    [InlineData("/", true)]
    [InlineData("/api/vehicles", true)]
    [InlineData("/wp-admin/install.php", true)]
    [InlineData("/assets/index-abc123.js", false)]
    [InlineData("/og.png", false)]
    [InlineData("/api/images/suv/1.jpg", false)]
    [InlineData("/robots.txt", false)]
    public void Only_pages_and_api_calls_count_not_the_files_a_page_is_made_of(string path, bool counts) =>
        Assert.Equal(counts, Hits.Counts(path));

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/128", "/api/vehicles", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1)", "/", true)]
    [InlineData("curl/8.4", "/", true)]
    [InlineData("", "/", true)]
    [InlineData(null, "/", true)]
    [InlineData("Mozilla/5.0 Chrome/128", "/wp-admin/install.php", true)]
    [InlineData("Mozilla/5.0 Chrome/128", "/.env", true)]
    public void A_scanner_is_told_by_its_agent_or_by_what_it_asked_for(string? agent, string path, bool bot) =>
        Assert.Equal(bot, Hits.LooksLikeABot(agent, path));

    [Fact]
    public void A_recorded_path_has_no_at_sign_and_no_tail()
    {
        Assert.Equal("/api/vehicles/someone%40example.com", Hits.PathOf("/api/vehicles/someone@example.com"));
        Assert.Equal(200, Hits.PathOf("/" + new string('a', 400)).Length);
    }

    // The site reading itself (1.0.3.11): its own tools by the mark on their
    // agent, App Service by its own agents; a browser, a crawler and nothing
    // at all are not the site.
    [Theory]
    [InlineData("TheYard-SelfRead/1 (activity-read)", true)]
    [InlineData("Mozilla/5.0 (Macintosh) AppleWebKit/605.1.15 Safari/605.1.15 TheYard-SelfRead/1 (card-shots)", true)]
    [InlineData("AlwaysOn", true)]
    [InlineData("ReadyForRequest/1.0 (HealthCheck)", true)]
    [InlineData("HealthCheck/1.0", true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/128", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_site_reading_itself_is_told_by_its_agent(string? agent, bool self) =>
        Assert.Equal(self, Hits.IsSelf(agent));

    [Fact]
    public void A_read_by_the_sites_own_tools_is_its_own_row_under_the_mark_and_not_a_person()
    {
        var tokens = new VisitorTokens("a signing key");
        var at = DateTimeOffset.Parse("2026-09-24T16:00:00Z");
        string token = tokens.TokenFor("203.0.113.7", at);

        var own = Hits.For(tokens, "203.0.113.7", at, token, "203.0.113.x", "/api/version", "sql", "TheYard-SelfRead/1 (sweep)");
        var person = Hits.For(tokens, "203.0.113.7", at, token, "203.0.113.x", "/api/version", "sql", "Mozilla/5.0 Chrome/128");

        Assert.Equal(Hits.SelfNetwork, own.Network);
        Assert.True(own.Bot);
        Assert.Matches("^[0-9a-f]{32}$", own.Visitor);
        Assert.NotEqual(token, own.Visitor);
        Assert.Equal("203.0.113.x", person.Network);
        Assert.False(person.Bot);
        Assert.Equal(token, person.Visitor);
    }

    // A port a forwarding hop wrote after the address is not part of it: it
    // made every one of App Service's own requests a new visitor.
    [Theory]
    [InlineData("127.0.0.1:8069", "127.0.0.1")]
    [InlineData("203.0.113.7:443", "203.0.113.7")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("[2001:db8::1]:443", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("::ffff:127.0.0.1", "::ffff:127.0.0.1")]
    public void A_port_after_the_address_is_dropped_before_the_token_and_the_network(string address, string expected) =>
        Assert.Equal(expected, VisitorTokens.WithoutPort(address));

    [Fact]
    public void The_forwarded_address_loses_its_port_so_one_machine_is_one_visitor()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1:15766";
        Assert.Equal("127.0.0.1", VisitorTokens.AddressOf(context));
        Assert.Equal("127.0.0.x", VisitorTokens.NetworkOf(VisitorTokens.AddressOf(context)));
    }
    // #endregion hits

    // #region who
    private static ActivityVisitor Row(string visitor, string network, int requests, int bots, string day = "2026-09-23", string store = "sql", string path = "/index.html") =>
        new(store, day, visitor, network, DateTimeOffset.Parse(day + "T10:00:00Z"), DateTimeOffset.Parse(day + "T11:00:00Z"), requests, bots,
            new Dictionary<string, int>(StringComparer.Ordinal) { [path] = requests });

    [Theory]
    [InlineData("127.0.0.x", true)]
    [InlineData("127.0.0.1:8069:x", true)]
    [InlineData("::1:x", true)]
    [InlineData("self:x", true)]
    [InlineData("107.138.48.x", false)]
    [InlineData("::ffff:x", false)]
    [InlineData("x", false)]
    public void A_kept_network_is_the_sites_own_when_it_is_the_mark_or_the_loopback(string network, bool self) =>
        Assert.Equal(self, Hits.IsSelfNetwork(network));

    [Fact]
    public void Every_visitor_day_is_one_kind_and_the_loopback_rows_of_a_day_are_one_machine()
    {
        var rows = new List<ActivityVisitor>
        {
            // App Service's own requests before 1.0.3.11: a new token for every port, one request each.
            Row("a1", "127.0.0.1:8069:x", 1, 0),
            Row("a2", "127.0.0.1:15766:x", 1, 0),
            Row("a3", "127.0.0.x", 1, 0, store: "cosmos"),
            // One of the site's own tools, under the mark.
            Row("t1", "self:x", 40, 40, path: "/api/version"),
            // A scanner: every request looked like a bot.
            Row("s1", "45.1.2.x", 9, 9, path: "/.env"),
            // A person on both stores, one of whose requests looked like a bot.
            Row("p1", "107.138.48.x", 6, 1, path: "/api/vehicles"),
            Row("p1", "107.138.48.x", 2, 0, store: "cosmos", path: "/api/docs/author"),
        };

        var count = ActivityWho.Count(rows);
        Assert.Equal((1, 1, 2), count);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ActivityWho.Summary(rows, ["sql", "cosmos"])));
        var who = json.RootElement;
        Assert.Equal(1, who.GetProperty("people").GetProperty("visitor_days").GetInt32());
        Assert.Equal(8, who.GetProperty("people").GetProperty("requests").GetInt32());
        Assert.Equal(1, who.GetProperty("scanners").GetProperty("visitor_days").GetInt32());
        Assert.Equal(9, who.GetProperty("scanners").GetProperty("requests").GetInt32());
        Assert.Equal(2, who.GetProperty("self").GetProperty("visitor_days").GetInt32());
        Assert.Equal(43, who.GetProperty("self").GetProperty("requests").GetInt32());
        Assert.Equal(4, who.GetProperty("all").GetProperty("visitor_days").GetInt32());
        Assert.Equal(60, who.GetProperty("all").GetProperty("requests").GetInt32());
        // What people asked for leaves out the scanner and the site's own reads.
        var peoplePaths = who.GetProperty("people").GetProperty("top_paths").EnumerateArray().Select(entry => entry.GetProperty("path").GetString()!).ToList();
        Assert.Equal(new[] { "/api/vehicles", "/api/docs/author" }, peoplePaths);
        // Per store: the person is one visitor-day on each, the loopback machine one on each it answered.
        var selfByStore = who.GetProperty("self").GetProperty("by_store").EnumerateArray().ToDictionary(entry => entry.GetProperty("store").GetString()!, entry => entry.GetProperty("visitor_days").GetInt32());
        Assert.Equal(2, selfByStore["sql"]);
        Assert.Equal(1, selfByStore["cosmos"]);
    }

    [Fact]
    public void A_visitor_day_is_counted_once_per_day_so_the_window_sums_the_days()
    {
        var rows = new List<ActivityVisitor>
        {
            Row("a1", "127.0.0.1:8069:x", 1, 0, day: "2026-09-22"),
            Row("a2", "127.0.0.1:8070:x", 1, 0, day: "2026-09-23"),
            Row("p1", "107.138.48.x", 3, 0, day: "2026-09-22"),
            Row("p1", "107.138.48.x", 3, 0, day: "2026-09-23"),
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ActivityWho.Summary(rows, ["sql"])));
        Assert.Equal(2, json.RootElement.GetProperty("people").GetProperty("visitor_days").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("self").GetProperty("visitor_days").GetInt32());
    }
    // #endregion who

    // #region key
    [Fact]
    public void The_admin_key_admits_the_whole_value_and_nothing_else()
    {
        var key = new AdminKey("open-sesame");
        Assert.True(key.Configured);
        Assert.True(key.Admits("open-sesame"));
        Assert.False(key.Admits("open-sesam"));
        Assert.False(key.Admits("open-sesame "));
        Assert.False(key.Admits(""));
        Assert.False(key.Admits(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("__ADMIN_KEY__")]
    public void An_unset_or_unsubstituted_key_admits_nobody(string? configured)
    {
        var key = new AdminKey(configured);
        Assert.False(key.Configured);
        Assert.False(key.Admits(configured));
        Assert.False(key.Admits("anything"));
    }
    // #endregion key

    // #region keeper
    /// <summary>
    /// Every batch goes to the keeper, whichever store served the hit, and the
    /// row keeps the serving store's key (14 September, after the serverless
    /// relational database spent its free month being written every five
    /// seconds). The other store records nothing.
    /// </summary>
    [Fact]
    public async Task Every_hit_goes_to_the_keeper_and_keeps_the_store_that_served_it()
    {
        var sql = new RecordingActivityStore();
        var cosmos = new RecordingActivityStore();
        var stores = new Dictionary<string, IActivityStore>(StringComparer.Ordinal) { ["sql"] = sql, ["cosmos"] = cosmos };
        var collector = new ActivityCollector(stores, "cosmos", NullLogger<ActivityCollector>.Instance);

        collector.Offer(Hit("sql", "2026-09-14T13:00:00Z", path: "/one"));
        collector.Offer(Hit("cosmos", "2026-09-14T13:00:00Z", path: "/two"));
        await collector.DrainAsync(CancellationToken.None);

        Assert.Equal("cosmos", collector.KeeperKey);
        Assert.Same(cosmos, collector.Keeper);
        Assert.Empty(sql.Recorded);
        Assert.Equal(new[] { "sql", "cosmos" }, cosmos.Recorded.Select(hit => hit.Store).ToList());
        Assert.Equal(2L, collector.Counters.Written);
        Assert.Equal(0L, collector.Counters.FailedBatches);
    }

    private sealed class RecordingActivityStore : IActivityStore
    {
        public List<ActivityHit> Recorded { get; } = [];

        public Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new ActivityAvailability(true, "recording"));

        public Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation)
        {
            Recorded.AddRange(hits);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<ActivityHour>>([]);

        public Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<ActivityVisitor>>([]);
    }
    // #endregion keeper
}

/// <summary>The endpoints on a real host, with a key set and the collector drained by hand.</summary>
public class ActivityEndpointTests : IClassFixture<ActivityEndpointTests.KeyedHost>
{
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Admin:Key", Key);
            // The rows are off by default (13 September); this host serves them so the endpoint can be held.
            builder.UseSetting("Admin:VisitorRows", "true");
        }
    }

    private readonly KeyedHost _host;
    private readonly HttpClient _client;

    public ActivityEndpointTests(KeyedHost host)
    {
        _host = host;
        _client = host.CreateClient();
    }

    private async Task VisitWithAnAddressInTheUrlAsync()
    {
        await _client.GetAsync("/api/vehicles?limit=1&who=someone@example.com");
        await _client.GetAsync("/api/vehicles/someone@example.com");
        await _client.GetAsync("/");
        await _host.Services.GetRequiredService<ActivityCollector>().DrainAsync(CancellationToken.None);
    }

    // #region endpoints
    [Fact]
    public async Task The_public_report_has_a_series_per_store_and_no_at_sign_after_an_address_was_offered()
    {
        await VisitWithAnAddressInTheUrlAsync();

        var response = await _client.GetAsync("/api/admin/activity?window=24h");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("24h", root.GetProperty("window").GetString());
        Assert.True(root.GetProperty("visitor_rows").GetBoolean());
        var stores = _host.Services.GetRequiredService<Backends>().All.Select(backend => backend.Key).ToHashSet();
        var series = root.GetProperty("series").EnumerateArray().Select(line => line.GetProperty("store").GetString()!).ToHashSet();
        Assert.Equal(stores, series);
        Assert.True(root.GetProperty("totals").GetProperty("requests").GetInt32() >= 3);
        // Not asserted: that the encoded path is among the top paths. On the
        // gate's Cosmos DB pass the hour document is shared with every test
        // that ran this hour and keeps twenty paths, so one hit is pruned; the
        // encoding itself is held by the Hits test above, and the at sign's
        // absence is held here, on the body, whatever the top paths are.
        Assert.Equal(25, root.GetProperty("series")[0].GetProperty("points").GetArrayLength());

        // Unique visitors per day: a day window spans today and yesterday, and
        // today has at least the one visitor this test is.
        var days = root.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(2, days.Count);
        var today = days[^1];
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), today.GetProperty("day").GetString());
        Assert.True(today.GetProperty("visitors").GetInt32() >= 1);
        Assert.Equal(stores.Count, today.GetProperty("by_store").GetArrayLength());
        Assert.Equal(today.GetProperty("visitors").GetInt32(), today.GetProperty("humans").GetInt32() + today.GetProperty("bots").GetInt32());

        // Who (1.0.3.11): the three kinds never add up to more than the day's
        // tokens (the loopback rows of a day are one machine), and the window's
        // all is the three together.
        int kinds = today.GetProperty("people").GetInt32() + today.GetProperty("scanners").GetInt32() + today.GetProperty("self").GetInt32();
        Assert.InRange(kinds, 1, today.GetProperty("visitors").GetInt32());
        var who = root.GetProperty("who");
        Assert.Equal(
            who.GetProperty("all").GetProperty("requests").GetInt32(),
            who.GetProperty("people").GetProperty("requests").GetInt32() + who.GetProperty("scanners").GetProperty("requests").GetInt32() + who.GetProperty("self").GetProperty("requests").GetInt32());
        Assert.True(who.GetProperty("all").GetProperty("requests").GetInt32() >= 3);
    }

    [Theory]
    [InlineData("7d", 29)]
    [InlineData("30d", 31)]
    public async Task The_longer_windows_are_bucketed_on_a_fixed_grid(string window, int atLeast)
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync($"/api/admin/activity?window={window}"));
        int points = json.RootElement.GetProperty("series")[0].GetProperty("points").GetArrayLength();
        Assert.InRange(points, atLeast, atLeast + 1);
    }

    [Fact]
    public async Task A_window_that_is_not_one_of_the_three_is_refused()
    {
        var response = await _client.GetAsync("/api/admin/activity?window=1y");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_visitor_rows_are_a_404_without_the_key_and_carry_no_at_sign_with_it()
    {
        await VisitWithAnAddressInTheUrlAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/activity/visitors")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/admin/activity/visitors?key=wrong")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/activity/visitors?window=24h");
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", body);

        using var json = JsonDocument.Parse(body);
        var rows = json.RootElement.GetProperty("visitors").EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Matches("^[0-9a-f]{32}$", row.GetProperty("visitor").GetString());
            Assert.EndsWith("x", row.GetProperty("network").GetString());
            Assert.False(row.TryGetProperty("email", out _));
            Assert.False(row.TryGetProperty("address", out _));
        }
    }

    [Fact]
    public async Task The_same_batch_twice_adds_rather_than_overwrites()
    {
        var store = _host.Services.GetRequiredService<Backends>().Default.Activity;
        Assert.True((await store.AvailabilityAsync(CancellationToken.None)).Available);
        // An hour nobody else writes to and a token nobody else uses, so the
        // count is this test's own on a store that outlives the run (the
        // gate's Cosmos DB pass keeps its test containers for a day).
        var at = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(Random.Shared.Next(1, 80_000));
        string token = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var hit = new ActivityHit(
            at, token, "198.51.100.x", "/twice", _host.Services.GetRequiredService<Backends>().Default.Key, false);

        await store.RecordAsync([hit], CancellationToken.None);
        await store.RecordAsync([hit, hit], CancellationToken.None);

        var hour = Assert.Single(await store.HoursAsync(at, CancellationToken.None), h => h.Hour == at && h.Store == hit.Store);
        Assert.Equal(3, hour.Requests);
        Assert.Equal(3, hour.Paths["/twice"]);
        var visitor = Assert.Single(await store.VisitorsAsync(at, CancellationToken.None), v => v.Visitor == hit.Visitor);
        Assert.Equal(3, visitor.Requests);
        Assert.Equal("198.51.100.x", visitor.Network);
    }
    // #endregion endpoints
}
