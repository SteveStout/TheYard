using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    // #endregion hits

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
}

/// <summary>The endpoints on a real host, with a key set and the collector drained by hand.</summary>
public class ActivityEndpointTests : IClassFixture<ActivityEndpointTests.KeyedHost>
{
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
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
