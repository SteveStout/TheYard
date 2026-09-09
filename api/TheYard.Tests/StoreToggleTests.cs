using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// One container, both stores, and which one a request gets (ADR: One
/// container, both stores, and its addendum on the toggle moving to the
/// sites). The rule is small and every branch of it is a visitor's
/// experience: the header wins, then the default, a name this container does
/// not have is never an error, and the cookie the old toggle set chooses
/// nothing any more and is expired on sight.
/// </summary>
public class StoreToggleTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static Backend Fake(string key, string name) => FakeBackend.Named(key, name);

    private static DefaultHttpContext Request(string? header = null, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers[Backends.HeaderName] = header;
        }
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{Backends.LegacyCookieName}={cookie}";
        }
        return context;
    }

    // #region rule
    [Fact]
    public void The_header_wins_then_the_default_and_the_old_cookie_chooses_nothing()
    {
        var both = new Backends([Fake("sql", "SQLite"), Fake("cosmos", "Azure Cosmos DB")], "sql");

        Assert.Equal("sql", both.For(null).Key);
        Assert.Equal("sql", both.For(Request()).Key);
        Assert.Equal("cosmos", both.For(Request(header: "COSMOS")).Key);
        // Until 1.0.0.100 a cookie chose the store. The toggle is a link
        // between the sites now, so each site is one store's site and a
        // browser that toggled last week lands where its address bar says.
        Assert.Equal("sql", both.For(Request(cookie: "cosmos")).Key);
        Assert.Equal("cosmos", both.For(Request(header: "cosmos", cookie: "sql")).Key);
    }

    [Fact]
    public void A_store_this_container_does_not_have_is_the_default_and_never_an_error()
    {
        var one = new Backends([Fake("sql", "SQLite")], "cosmos");

        Assert.Equal("sql", one.Default.Key);
        Assert.Equal("sql", one.For(Request(header: "cosmos")).Key);
        Assert.Equal("sql", one.For(Request(header: "anything")).Key);
        Assert.Null(one.Named("cosmos"));
    }

    [Fact]
    public void The_old_cookie_is_expired_when_seen_and_left_alone_when_absent()
    {
        // StringValues casts itself to a string when handed to an assertion
        // that takes one, so the header is read as the array it is.
        var carrying = Request(cookie: "cosmos");
        Backends.ExpireLegacyCookie(carrying);
        string?[] setCookie = carrying.Response.Headers.SetCookie.ToArray();
        string? expired = Assert.Single(setCookie);
        Assert.StartsWith(Backends.LegacyCookieName + "=;", expired);
        Assert.Contains("expires=", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", expired, StringComparison.OrdinalIgnoreCase);

        // A browser that never had the cookie is not handed a Set-Cookie for
        // every page load.
        var clean = Request();
        Backends.ExpireLegacyCookie(clean);
        string?[] none = clean.Response.Headers.SetCookie.ToArray();
        Assert.Empty(none);
    }

    [Fact]
    public void The_default_is_the_named_store_or_the_first_one()
    {
        var named = new Backends([Fake("sql", "SQLite"), Fake("cosmos", "Azure Cosmos DB")], "cosmos");
        var unnamed = new Backends([Fake("sql", "SQLite"), Fake("cosmos", "Azure Cosmos DB")], null);

        Assert.Equal("cosmos", named.Default.Key);
        Assert.Equal("sql", unnamed.Default.Key);
        Assert.Throws<ArgumentException>(() => new Backends([], null));
    }

    [Fact]
    public void The_other_site_is_an_http_origin_or_nothing()
    {
        var stores = new[] { Fake("sql", "SQLite"), Fake("cosmos", "Azure Cosmos DB") };

        // The address a visitor types, trimmed to its origin: the bar links to
        // the site, not to whatever path the setting happened to carry.
        Assert.Equal("https://theyard.stevenstout.biz", new Backends(stores, "sql", "https://theyard.stevenstout.biz/").OtherSite);
        Assert.Equal("http://theyard-ss.westus2.azurecontainer.io:8080", new Backends(stores, "sql", "http://theyard-ss.westus2.azurecontainer.io:8080/api/stores").OtherSite);
        Assert.Equal("https://theyard.stevenstout.biz", new Backends(stores, "sql", "https://theyard.stevenstout.biz").Describe(null).OtherSite);

        // No setting, an empty one, a relative path, or a scheme a browser
        // would not follow to a site: no link, never an error.
        Assert.Null(new Backends(stores, "sql").OtherSite);
        Assert.Null(new Backends(stores, "sql", "").OtherSite);
        Assert.Null(new Backends(stores, "sql", "/somewhere").OtherSite);
        Assert.Null(new Backends(stores, "sql", "javascript:alert(1)").OtherSite);
        Assert.Null(new Backends(stores, "sql", "ftp://files.example.com").OtherSite);
    }

    // #endregion rule

    // #region endpoints
    /// <summary>
    /// What this run's container answers: one store on SQLite alone, two with
    /// the document store configured and made the default. The tests below
    /// read that answer rather than assuming either shape, because the ship
    /// gate runs them both ways.
    /// </summary>
    private static async Task<JsonElement> StoresOf(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/stores");

    [Fact]
    public async Task The_page_is_told_which_stores_there_are_and_which_one_it_is_on()
    {
        var client = factory.CreateClient();

        var stores = await StoresOf(client);

        var listed = stores.GetProperty("stores").EnumerateArray().ToList();
        Assert.Contains(listed, store => store.GetProperty("key").GetString() == "sql"
            && store.GetProperty("name").GetString() == "SQLite"
            && store.GetProperty("ready").GetBoolean());
        // The current store of a request that names none is the default, and
        // exactly one store is the default.
        var byDefault = Assert.Single(listed, store => store.GetProperty("default").GetBoolean());
        Assert.Equal(byDefault.GetProperty("key").GetString(), stores.GetProperty("current").GetString());
        // No other site is configured here, and the page is told so rather
        // than left to guess from a missing field.
        Assert.Equal(JsonValueKind.Null, stores.GetProperty("other_site").ValueKind);
    }

    [Fact]
    public async Task A_visit_carrying_the_old_cookie_lands_on_the_default_and_leaves_without_it()
    {
        var client = factory.CreateClient();
        var stores = await StoresOf(client);
        string byDefault = stores.GetProperty("stores").EnumerateArray()
            .Single(store => store.GetProperty("default").GetBoolean()).GetProperty("key").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stores");
        request.Headers.Add("Cookie", $"{Backends.LegacyCookieName}=cosmos");
        var answered = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal(byDefault, (await answered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("current").GetString());
        string expired = Assert.Single(answered.Headers.GetValues("Set-Cookie"), value => value.StartsWith(Backends.LegacyCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("expires=", expired, StringComparison.OrdinalIgnoreCase);

        // The switch endpoint went with the cookie. A stale page that still
        // posts to it gets a refusal and nothing is set: 405 rather than 404,
        // because the page's own fallback answers GET on every path, so the
        // path exists and the method does not.
        var gone = await client.PostAsJsonAsync("/api/stores/select", new { store = byDefault });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, gone.StatusCode);
        Assert.DoesNotContain(gone.Headers, header => header.Key == "Set-Cookie");
    }

    [Fact]
    public async Task A_request_that_names_a_store_is_served_by_it_and_filed_under_it()
    {
        var client = factory.CreateClient();
        var stores = await StoresOf(client);
        // The relational store is always there, and naming it by header is how
        // a measurement asks for a store without a browser.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/facets");
        request.Headers.Add(Backends.HeaderName, "sql");
        var answered = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        var metrics = await client.GetFromJsonAsync<JsonElement>("/api/admin/metrics");

        var listed = metrics.GetProperty("backends").EnumerateArray().ToList();
        Assert.Equal(stores.GetProperty("stores").GetArrayLength(), listed.Count);
        var sql = Assert.Single(listed, backend => backend.GetProperty("key").GetString() == "sql");
        Assert.True(sql.GetProperty("requests").GetProperty("window").GetInt32() >= 1);
        Assert.Equal("SQLite", sql.GetProperty("store").GetString());
        // The default store was warmed before the first request; the other
        // warms on its first request or in the background, so only the
        // default's cold start is promised here.
        var byDefault = Assert.Single(listed, backend => backend.GetProperty("default").GetBoolean());
        Assert.True(byDefault.GetProperty("startup").GetProperty("ready_ms").GetInt64() > 0);
    }
    // #endregion endpoints
}
