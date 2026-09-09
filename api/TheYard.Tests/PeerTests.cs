using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The peer is a thing that can be down (ADR: Backends, side by side). These
/// were written in the order the record asks for: the failures first, the
/// happy path last.
/// </summary>
public class PeerTests
{
    private static WebApplicationFactory<Program> Api(string? peerUrl) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (peerUrl is not null)
            {
                builder.UseSetting("Peer:Url", peerUrl);
            }
        });

    // #region peer-down
    [Fact]
    public async Task With_no_peer_configured_the_endpoint_says_so_and_the_rest_of_admin_is_untouched()
    {
        await using var api = Api(null);
        var client = api.CreateClient();

        var peer = await client.GetFromJsonAsync<JsonElement>("/api/admin/peer");
        Assert.False(peer.GetProperty("configured").GetBoolean());
        Assert.False(peer.GetProperty("reachable").GetBoolean());
        Assert.Contains("no peer", peer.GetProperty("reason").GetString());

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task A_placeholder_the_deploy_left_behind_is_no_peer()
    {
        await using var api = Api("__PEER_URL__");
        var peer = await api.CreateClient().GetFromJsonAsync<JsonElement>("/api/admin/peer");
        Assert.False(peer.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task An_unreachable_peer_is_a_sentence_within_the_patience_not_a_hang()
    {
        // Port 9 is discard; nothing listens there and the connection is refused.
        await using var api = Api("http://127.0.0.1:9");
        var client = api.CreateClient();

        var clock = Stopwatch.StartNew();
        var peer = await client.GetFromJsonAsync<JsonElement>("/api/admin/peer");
        clock.Stop();

        Assert.True(peer.GetProperty("configured").GetBoolean());
        Assert.False(peer.GetProperty("reachable").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(peer.GetProperty("reason").GetString()));
        // A refused connection answers in milliseconds on an idle machine. The
        // margin is for the suite, not the code: with two stores booting in
        // every application at once, a timer's callback and a refused socket
        // both wait their turn on the thread pool, and this took 4.6 seconds
        // once (ADR: One container, both stores). The claim is "not a hang",
        // which ten seconds still holds against a peer that never answers.
        Assert.True(clock.Elapsed < PeerReader.Patience + TimeSpan.FromSeconds(8), $"took {clock.Elapsed}");
        // The type of the failure, never its message: a message names a host and a port.
        Assert.DoesNotContain("127.0.0.1:9", peer.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_peer_that_answers_slowly_is_reported_as_slow_and_nothing_waits_for_it()
    {
        // Twenty seconds, against a patience of two and a half. The assertion
        // below is generous on purpose: what it proves is that nothing waits
        // for the peer's full answer, and a tight number would measure the
        // machine, which under the whole suite in parallel once took four
        // seconds to fire a timer (ADR: The SQL Server backend, the addendum on
        // the CI failure that could not explain itself).
        using var slow = new CannedPeer(delay: TimeSpan.FromSeconds(20));
        await using var api = Api(slow.Origin);
        // The client first, so the clock measures the peer read and not the
        // host coming up: creating the client is what boots Program.cs, and
        // that migrates, seeds and indexes a hundred thousand vehicles.
        var client = api.CreateClient();

        var clock = Stopwatch.StartNew();
        var peer = await client.GetFromJsonAsync<JsonElement>("/api/admin/peer");
        clock.Stop();

        Assert.False(peer.GetProperty("reachable").GetBoolean());
        Assert.Contains("did not answer", peer.GetProperty("reason").GetString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
    }
    // #endregion peer-down

    // #region peer-up
    [Fact]
    public async Task A_peer_that_answers_is_relayed_whole_with_its_host_and_nothing_else_about_its_address()
    {
        using var canned = new CannedPeer(delay: TimeSpan.Zero);
        await using var api = Api(canned.Origin);

        var peer = await api.CreateClient().GetFromJsonAsync<JsonElement>("/api/admin/peer");

        Assert.True(peer.GetProperty("reachable").GetBoolean());
        Assert.Equal("127.0.0.1", peer.GetProperty("host").GetString());
        Assert.Equal("Azure Cosmos DB", peer.GetProperty("metrics").GetProperty("store").GetProperty("store").GetString());
        Assert.Equal(1, canned.Hits);
    }
    // #endregion peer-up

    // #region routes
    [Theory]
    [InlineData("/api/vehicles/abc-123", "/api/vehicles/{id}")]
    [InlineData("/api/vehicles/abc-123/bids", "/api/vehicles/{id}/bids")]
    [InlineData("/api/vehicles/abc-123/buy-now", "/api/vehicles/{id}/buy-now")]
    [InlineData("/api/vehicles", "/api/vehicles")]
    [InlineData("/api/docs/adr-lockout", "/api/docs/{slug}")]
    [InlineData("/api/docs/diagrams/dataflow", "/api/docs/diagrams/{name}")]
    [InlineData("/api/images/suv-01.jpg", "/api/images/{file}")]
    [InlineData("/api/auth/login", "/api/auth/login")]
    public void A_route_is_a_path_with_its_identifiers_taken_out(string path, string template) =>
        Assert.Equal(template, Routes.TemplateOf(path));
    // #endregion routes

    // #region charges
    [Fact]
    public void Two_requests_to_one_path_are_two_samples_not_one_sum()
    {
        // The first version grouped by the path string and reported twenty
        // sign-ins as one request that cost 34 RU. The trace identifier is what
        // tells them apart; the path is what names the route.
        StoreOperation Op(string id, double charge) => new(
            DateTimeOffset.UtcNow, "users", "point read", "ReadItem", [], "pinned to the account", 1, charge, 1, "200", "POST /api/auth/login", id);
        var charges = Routes.ChargesByRoute([Op("req-1", 1), Op("req-1", 1), Op("req-2", 1), Op("req-2", 1)]);

        var login = Assert.Single(charges);
        Assert.Equal("POST /api/auth/login", login.Route);
        Assert.Equal(2, login.Requests);
        Assert.Equal(2, login.RuP50);
        Assert.Equal(2, login.OperationsPerRequest);
    }
    // #endregion charges

    /// <summary>A peer on the loopback that answers /api/admin/metrics with one canned document, after a delay if asked.</summary>
    private sealed class CannedPeer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        public int Hits;

        public CannedPeer(TimeSpan delay)
        {
            int port = FreePort();
            Origin = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add($"{Origin}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); }
                    catch (Exception) { return; }
                    Interlocked.Increment(ref Hits);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(delay, _stop.Token);
                            byte[] body = Encoding.UTF8.GetBytes("""{"store":{"store":"Azure Cosmos DB","window":3}}""");
                            context.Response.ContentType = "application/json";
                            await context.Response.OutputStream.WriteAsync(body);
                        }
                        catch (Exception)
                        {
                            // The listener is stopping, or the caller gave up first.
                        }
                        finally
                        {
                            try { context.Response.Close(); } catch (Exception) { }
                        }
                    });
                }
            });
        }

        public string Origin { get; }

        private static int FreePort()
        {
            using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
        }
    }
}

/// <summary>The experiment card's fourth empty state: a relational container has nothing to run it against (ADR: The partition key).</summary>
public class ExperimentEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // #region experiment-empty
    [Fact]
    public async Task With_nothing_to_query_the_experiment_says_so_and_shows_nothing()
    {
        // Two of the card's empty states, depending on which store the suite
        // is booted on: a relational container has no document store to ask,
        // and the test containers on the document store have no catalogue in
        // them. Both are a sentence and an empty list, never an error.
        var client = factory.CreateClient();
        var store = await client.GetFromJsonAsync<JsonElement>("/api/admin/store");
        bool cosmos = store.GetProperty("store").GetString() == "Azure Cosmos DB";

        var answer = await client.GetFromJsonAsync<JsonElement>("/api/admin/experiment");
        Assert.False(answer.GetProperty("available").GetBoolean());
        Assert.Contains(cosmos ? "catalogue container" : "not on Azure Cosmos DB", answer.GetProperty("reason").GetString());
        Assert.Equal(0, answer.GetProperty("rows").GetArrayLength());
    }
    // #endregion experiment-empty
}
