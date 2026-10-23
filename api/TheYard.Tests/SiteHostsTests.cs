using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TheYard.Tests;

/// <summary>
/// One web app answers to three names (ADR: One web app for every .NET process): the store a
/// request gets when it names none follows the name it arrived on, the header that names a store
/// still wins, a name the process does not know is no name at all, and The Shed's name reaches
/// The Shed's own application in the same process.
/// </summary>
public class SiteHostsTests
{
    private static readonly SiteHosts Three = new([new Site("theyard.example", "sql"), new Site("theyard-cosmos.example", "cosmos"), new Site("theshed.example", null)]);

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
        // A name App Service routed by wins over the header: The Shed's domain cannot be told it is a Yard site.
        Assert.Equal("theshed.example", Three.Of(Arriving("theshed.example", edgeSaid: "theyard.example")));
        Assert.Equal("theshed.example", Three.GuestOf(Arriving("theshed.example", edgeSaid: "theyard-cosmos.example")));
        Assert.Null(Three.SiteOrigin(Arriving(edgeSaid: "theshed.example")));
        Assert.Equal("https://theyard-cosmos.example", Three.SiteOrigin(Arriving(edgeSaid: "theyard-cosmos.example")));
        Assert.Equal("theshed.example", Three.GuestOf(Arriving("theshed.example")));
        Assert.Null(Three.GuestOf(Arriving("theyard.example")));
        // A deploy's unreplaced placeholder and an empty setting are no name.
        var none = new SiteHosts([new Site("__HOSTS_SQL__", "sql"), new Site("", "cosmos"), new Site("theshed.example", null)]);
        Assert.Null(none.HostFor("sql"));
        Assert.Null(none.HostFor("cosmos"));
        Assert.Single(none.All);
        Assert.Null(SiteHosts.None.GuestOf(Arriving("theshed.example")));
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
        // The gate runs this both ways: on SQLite alone there is no Cosmos DB store to pick, and
        // the name falls back to the one there is; with Cosmos DB configured the name must pick it.
        bool hasCosmos = stores.GetProperty("stores").EnumerateArray().Any(store => store.GetProperty("key").GetString() == "cosmos");
        Assert.Equal(hasCosmos ? "cosmos" : "sql", stores.GetProperty("current").GetString());
        // The Shed's name by its Host header, the way App Service routes it, with a Yard name in the
        // edge's header: still The Shed.
        using var byHost = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        byHost.Headers.Host = "theshed.example";
        byHost.Headers.Add(SiteHosts.HeaderName, "theyard.example");
        Assert.Contains("\"healthy\"", await (await client.SendAsync(byHost)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_sheds_folder_its_name_answers_503_and_the_yard_carries_on()
    {
        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Hosts:Shed", "theshed.example");
            builder.UseSetting("Shed:ContentRoot", Path.Combine(Path.GetTempPath(), $"no-shed-{Guid.NewGuid():N}"));
        });
        var client = api.CreateClient();

        var shed = await Get(client, "/", "theshed.example");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, shed.StatusCode);
        Assert.Contains("is not running in this process", await shed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/version")).StatusCode);
    }
    // #endregion shed-mounted

    [Fact]
    public void The_sites_are_a_configured_list_and_the_settings_named_per_site_read_as_the_same_list()
    {
        // The list: any number of sites, each a name and the store it serves, a guest with none.
        var listed = SiteSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sites:0:Host"] = "theyard.example",
            ["Sites:0:Store"] = "sql",
            ["Sites:1:Host"] = "THEYARD-COSMOS.example.",
            ["Sites:1:Store"] = "cosmos",
            ["Sites:2:Host"] = "theshed.example",
        }).Build());
        // A web app configured before the list carries one setting per site; it reads the same.
        var named = SiteSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosts:Sql"] = "theyard.example",
            ["Hosts:Cosmos"] = "theyard-cosmos.example",
            ["Hosts:Shed"] = "theshed.example",
        }).Build());
        foreach (var hosts in new[] { listed, named })
        {
            Assert.Equal(["theyard.example", "theyard-cosmos.example", "theshed.example"], hosts.All.Select(site => site.Host));
            Assert.Equal("cosmos", hosts.StoreFor("theyard-cosmos.example"));
            Assert.Equal("https://theyard.example", hosts.OriginOf("sql"));
            Assert.Equal(["theshed.example"], hosts.Guests);
        }
    }

    [Fact]
    public async Task Every_answer_names_the_web_app_that_gave_it_only_where_App_Service_says_which()
    {
        await using var named = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("WEBSITE_SITE_NAME", "APP-THEYARD-ONE-SS"));
        var answer = await named.CreateClient().GetAsync("/api/version");
        Assert.Equal("APP-THEYARD-ONE-SS", answer.Headers.GetValues(EdgeKeeps.SiteHeader).Single());

        await using var plain = new WebApplicationFactory<Program>();
        Assert.False((await plain.CreateClient().GetAsync("/api/version")).Headers.Contains(EdgeKeeps.SiteHeader));
    }
}
