using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// One container, both stores, and the toggle that picks one per request
/// (ADR: One container, both stores). The rule is small and every branch of
/// it is a visitor's experience: the header wins, then the cookie, then the
/// default, and a name this container does not have is never an error.
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
            context.Request.Headers.Cookie = $"{Backends.CookieName}={cookie}";
        }
        return context;
    }

    // #region rule
    [Fact]
    public void The_header_wins_then_the_cookie_then_the_default()
    {
        var both = new Backends([Fake("sql", "SQLite"), Fake("cosmos", "Azure Cosmos DB")], "sql");

        Assert.Equal("sql", both.For(null).Key);
        Assert.Equal("sql", both.For(Request()).Key);
        Assert.Equal("cosmos", both.For(Request(cookie: "cosmos")).Key);
        Assert.Equal("sql", both.For(Request(header: "sql", cookie: "cosmos")).Key);
        Assert.Equal("cosmos", both.For(Request(header: "COSMOS")).Key);
    }

    [Fact]
    public void A_store_this_container_does_not_have_is_the_default_and_never_an_error()
    {
        var one = new Backends([Fake("sql", "SQLite")], "cosmos");

        Assert.Equal("sql", one.Default.Key);
        Assert.Equal("sql", one.For(Request(cookie: "cosmos")).Key);
        Assert.Equal("sql", one.For(Request(header: "anything")).Key);
        Assert.Null(one.Named("cosmos"));
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
    }

    [Fact]
    public async Task Choosing_a_store_sets_the_cookie_and_choosing_one_that_is_not_here_is_a_sentence()
    {
        var client = factory.CreateClient();
        var stores = await StoresOf(client);
        string here = stores.GetProperty("stores").EnumerateArray().First().GetProperty("key").GetString()!;

        var chosen = await client.PostAsJsonAsync("/api/stores/select", new { store = here });
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        string cookie = Assert.Single(chosen.Headers.GetValues("Set-Cookie"), value => value.StartsWith(Backends.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(here, (await chosen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("current").GetString());

        var refused = await client.PostAsJsonAsync("/api/stores/select", new { store = "nowhere" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("SQLite", problem.GetProperty("detail").GetString());
        Assert.Contains("nowhere", problem.GetProperty("detail").GetString());
        Assert.DoesNotContain(refused.Headers, header => header.Key == "Set-Cookie");
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
