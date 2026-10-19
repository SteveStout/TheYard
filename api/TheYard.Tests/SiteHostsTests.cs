using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// One web app answers to three names (ADR: One web app for every .NET process): the store a
/// request gets when it names none follows the name it arrived on, the header that names a store
/// still wins, a name the process does not know is no name at all, and The Shed's name reaches
/// The Shed's own application in the same process.
/// </summary>
public class SiteHostsTests
{
    private static readonly SiteHosts Three = new("theyard.example", "theyard-cosmos.example", "theshed.example");

    private static DefaultHttpContext Arriving(string? host = null, string? edgeSaid = null, string? store = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host ?? "app-theyard-one-ss-x.azurewebsites.net");
        if (edgeSaid is not null)
        {
            context.Request.Headers[SiteHosts.HeaderName] = edgeSaid;
        }
        if (store is not null)
        {
            context.Request.Headers[Backends.HeaderName] = store;
        }
        return context;
    }

    // #region rule
    [Fact]
    public void The_name_a_request_arrived_on_picks_its_store_and_the_store_header_still_wins()
    {
        var both = new Backends([FakeBackend.Named("sql", "Azure SQL Database"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql", hosts: Three);

        // Bound straight to the web app: the Host header is the name.
        Assert.Equal("cosmos", both.For(Arriving("theyard-cosmos.example")).Key);
        Assert.Equal("sql", both.For(Arriving("THEYARD.example")).Key);
        // Through the edge: the origin's own name, and the edge says which name the visitor typed.
        Assert.Equal("cosmos", both.For(Arriving(edgeSaid: "theyard-cosmos.example")).Key);
        Assert.Equal("sql", both.For(Arriving(edgeSaid: "theyard.example")).Key);
        // The origin's own name with nothing said, a health probe or a test: the default.
        Assert.Equal("sql", both.For(Arriving()).Key);
        // The header that names a store wins over the name, which is how the proof measures both.
        Assert.Equal("sql", both.For(Arriving("theyard-cosmos.example", store: "sql")).Key);
        // The site's default is the one marked for the page, and the other site's name is the link.
        var cosmosView = both.Describe(Arriving(edgeSaid: "theyard-cosmos.example"));
        Assert.Equal("cosmos", Assert.Single(cosmosView.Stores, store => store.Default).Key);
        Assert.Equal("https://theyard.example", cosmosView.OtherSite);
        Assert.Equal("https://theyard-cosmos.example", both.Describe(Arriving("theyard.example")).OtherSite);
    }

    [Fact]
    public void A_name_the_process_does_not_know_is_no_name_and_an_unset_name_is_none()
    {
        // A header is what the sender says it is, so it counts only when it names a configured name:
        // the most a sender can choose is which of the three sites answers.
        Assert.Null(Three.Of(Arriving(edgeSaid: "evil.example")));
        Assert.Equal("theyard.example", Three.Of(Arriving("theyard.example", edgeSaid: "evil.example")));
        Assert.Null(Three.SiteOrigin(Arriving(edgeSaid: "theshed.example")));
        Assert.Equal("https://theyard-cosmos.example", Three.SiteOrigin(Arriving(edgeSaid: "theyard-cosmos.example")));
        Assert.True(Three.IsShed(Arriving("theshed.example")));
        Assert.False(Three.IsShed(Arriving("theyard.example")));
        // A deploy's unreplaced placeholder and an empty setting are no name.
        var none = new SiteHosts("__HOSTS_SQL__", "", null);
        Assert.Null(none.Sql);
        Assert.Null(none.Cosmos);
        Assert.False(SiteHosts.None.IsShed(Arriving("theshed.example")));
    }
    // #endregion rule

    // #region shed-mounted
    /// <summary>
    /// The process with the three names and The Shed's folder, the way the deploy sets them, and
    /// The Shed's folder in this checkout as its content root.
    /// </summary>
    private static WebApplicationFactory<Program> Mounted() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Hosts:Sql", "theyard.example");
            builder.UseSetting("Hosts:Cosmos", "theyard-cosmos.example");
            builder.UseSetting("Hosts:Shed", "theshed.example");
            builder.UseSetting("Shed:ContentRoot", Path.Combine(Repo.Root(), "samples", "theshed"));
        });

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(SiteHosts.HeaderName, host);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task The_sheds_name_reaches_the_sheds_own_page_health_and_api_and_the_yard_is_untouched()
    {
        await using var api = Mounted();
        // HTTPS, because The Shed in Development redirects plain HTTP, as it does on a developer's machine.
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var page = await (await Get(client, "/", "theshed.example")).Content.ReadAsStringAsync();
        Assert.Contains("<title>The Shed</title>", page, StringComparison.Ordinal);
        var health = await Get(client, "/healthz", "theshed.example");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"healthy\"", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // The Shed's own JSON shape (snake_case, from its own container) on its own version route.
        var shedVersion = await (await Get(client, "/api/version", "theshed.example")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(shedVersion.TryGetProperty("version", out _));

        // The same paths on the Yard's names are TheYard's.
        var yardPage = await (await Get(client, "/index.html", "theyard.example")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<title>The Shed</title>", yardPage, StringComparison.Ordinal);
        var stores = await (await Get(client, "/api/stores", "theyard-cosmos.example")).Content.ReadFromJsonAsync<JsonElement>();
        bool hasCosmos = stores.GetProperty("stores").EnumerateArray().Any(store => store.GetProperty("key").GetString() == "cosmos");
        Assert.Equal(hasCosmos ? "cosmos" : "sql", stores.GetProperty("current").GetString());
    }

    [Fact]
    public async Task Without_the_sheds_folder_nothing_is_mounted_and_the_name_is_the_yards()
    {
        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Hosts:Shed", "theshed.example");
            builder.UseSetting("Shed:ContentRoot", Path.Combine(Path.GetTempPath(), $"no-shed-{Guid.NewGuid():N}"));
        });
        var client = api.CreateClient();

        var page = await (await Get(client, "/index.html", "theshed.example")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<title>The Shed</title>", page, StringComparison.Ordinal);
    }
    // #endregion shed-mounted
}
