using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The performance proof (ADR: Same performance, proven): the arithmetic that
/// turns samples into a verdict, held without a store; and the endpoints,
/// held against whichever stores this run's container has.
/// </summary>
public class ProofTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // #region verdict-tests
    [Theory]
    [InlineData(0, 100, true)]
    [InlineData(15, 100, true)]
    [InlineData(-15, 20, true)]
    [InlineData(16, 20, false)]
    [InlineData(30, 200, true)]
    [InlineData(31, 200, false)]
    public void Same_means_within_fifteen_milliseconds_or_fifteen_per_cent_of_the_slower(long difference, long slower, bool same) =>
        Assert.Equal(same, ProofResult.Same(difference, slower));

    private static ProofRunner.StoreRun Run(string key, string name, long hopMs, params (string Path, long Ms, int Operations)[] samples)
    {
        var run = new ProofRunner.StoreRun(FakeBackend.Named(key, name)) { HopMs = hopMs };
        foreach (var (path, ms, operations) in samples)
        {
            run.Samples.Add(new ProofRunner.Sample(path, ms, 200, operations, 0));
        }
        return run;
    }

    [Fact]
    public void The_verdict_is_the_same_when_the_paired_medians_agree_and_names_the_leader_when_they_do_not()
    {
        var sql = Run("sql", "SQLite", 1,
            ("listing", 100, 0), ("listing", 104, 0), ("listing", 98, 0),
            ("bid", 80, 2), ("bid", 82, 2), ("bid", 79, 2));
        var cosmos = Run("cosmos", "Azure Cosmos DB", 1,
            ("listing", 101, 0), ("listing", 99, 0), ("listing", 103, 0),
            ("bid", 20, 2), ("bid", 21, 2), ("bid", 19, 2));

        var result = ProofResult.Of(DateTimeOffset.UtcNow, 3, [sql, cosmos]);

        Assert.Equal("done", result.Status);
        var listing = Assert.Single(result.Rows, row => row.Path == "listing");
        Assert.Equal("the same", listing.Verdict);
        Assert.Equal(3, listing.Cells[0].Samples);
        var bid = Assert.Single(result.Rows, row => row.Path == "bid");
        Assert.StartsWith("Azure Cosmos DB leads by", bid.Verdict, StringComparison.Ordinal);
        Assert.True(bid.MedianDifferenceMs < -50);
        // Nothing was measured for the paths that had no samples, and the
        // sentence counts only what was.
        Assert.Equal("not measured", Assert.Single(result.Rows, row => row.Path == "facets").Verdict);
        Assert.Contains("1 of 2 paths", result.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_difference_that_is_only_the_round_trip_to_the_store_is_said_to_be_that()
    {
        // The relational store is forty milliseconds away and a bid runs two
        // statements; the document store is next door. Eighty milliseconds
        // of difference, all of it distance.
        var sql = Run("sql", "Azure SQL Database", 40, ("bid", 100, 2), ("bid", 101, 2), ("bid", 99, 2));
        var cosmos = Run("cosmos", "Azure Cosmos DB", 2, ("bid", 24, 2), ("bid", 25, 2), ("bid", 23, 2));

        var result = ProofResult.Of(DateTimeOffset.UtcNow, 3, [sql, cosmos]);

        var bid = Assert.Single(result.Rows, row => row.Path == "bid");
        Assert.Equal(-76, bid.MedianDifferenceMs);
        Assert.Equal(0, bid.DifferenceWithoutHopsMs);
        Assert.EndsWith("all of it the round trip to the store", bid.Verdict, StringComparison.Ordinal);
        Assert.Contains("the difference is the round trip to the store", result.Sentence, StringComparison.Ordinal);
    }
    // #endregion verdict-tests

    // #region canned-run
    /// <summary>
    /// A whole run against a container that answers from a script, so every
    /// step of a round is exercised without a store: the session cookie is
    /// kept per store and sent back, the vehicle is chosen from the listing,
    /// the bid's answer feeds the raise, and the samples land where the
    /// verdict reads them. The real run against real stores is the endpoint
    /// test below, on the ship gate's two-store shape.
    /// </summary>
    private sealed class CannedYard : HttpMessageHandler
    {
        public List<(string Store, string Method, string Path, string? Cookie)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string store = request.Headers.TryGetValues(Backends.HeaderName, out var named) ? named.First() : "?";
            string? cookie = request.Headers.TryGetValues("Cookie", out var cookies) ? cookies.First() : null;
            Seen.Add((store, request.Method.Method, path, cookie));

            static HttpResponseMessage Json(object body) =>
                new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

            HttpResponseMessage response = (request.Method.Method, path) switch
            {
                ("POST", "/api/auth/register") or ("POST", "/api/auth/login") => WithSession(Json(new { signed_in = true }), store),
                ("GET", "/api/vehicles") => Json(new
                {
                    total = 2,
                    vehicles = new object[]
                    {
                        // Ends in a minute: not this one.
                        new { id = "soon", starting_bid = 5000, current_bid = (int?)null, buy_now_price = (int?)null, auction_ends_at = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds() },
                        new { id = "v1", starting_bid = 5000, current_bid = (int?)null, buy_now_price = (int?)null, auction_ends_at = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds() },
                    },
                }),
                ("GET", "/api/vehicles/v1") => Json(new { id = "v1", min_next_bid = 5100 }),
                ("GET", "/api/facets") => Json(new { makes = Array.Empty<string>() }),
                ("POST", "/api/vehicles/v1/bids") => Json(new { kind = "accepted", amount = 5100, vehicle = new { min_next_bid = 5200 } }),
                ("DELETE", "/api/bids") => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
            return Task.FromResult(response);
        }

        private static HttpResponseMessage WithSession(HttpResponseMessage response, string store)
        {
            response.Headers.Add("Set-Cookie", $"{TokenIssuer.CookieName}=token-{store}; path=/; httponly");
            return response;
        }
    }

    [Fact]
    public async Task A_run_walks_every_step_of_a_round_on_both_stores_and_keeps_one_session_per_store()
    {
        var yard = new CannedYard();
        var backends = new Backends([FakeBackend.Named("sql", "SQLite"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql");
        var runner = new ProofRunner(
            backends,
            new ProofClients(() => new HttpClient(yard) { BaseAddress = new Uri("http://yard.test") }),
            new SqlRingBuffer(10),
            new StoreRingBuffer(10));

        var result = await runner.RunAsync(2);

        Assert.Equal("done", result.Status);
        Assert.Equal(2, result.Rounds);
        Assert.Equal(new[] { "sql", "cosmos" }, result.Stores.Select(store => store.Key));
        // One registration per store, two of everything else, all answered.
        var register = Assert.Single(result.Rows, row => row.Path == "register");
        Assert.All(register.Cells, cell => Assert.Equal(1, cell.Samples));
        foreach (var path in ProofRunner.Paths)
        {
            var row = Assert.Single(result.Rows, candidate => candidate.Path == path.Key);
            Assert.All(row.Cells, cell => Assert.Equal(2, cell.Samples));
            Assert.All(row.Cells, cell => Assert.Equal(0, cell.Failures));
            Assert.NotNull(row.MedianDifferenceMs);
        }
        // The raise was placed at the amount the bid's answer named, and the
        // vehicle with a minute left was passed over.
        Assert.Contains(yard.Seen, seen => seen.Method == "POST" && seen.Path == "/api/vehicles/v1/bids");
        Assert.DoesNotContain(yard.Seen, seen => seen.Path.Contains("/soon/", StringComparison.Ordinal));
        // The session from each store's registration rode on that store's
        // later requests and never on the other store's.
        Assert.All(yard.Seen.Where(seen => seen.Path == "/api/bids"), seen =>
            Assert.Equal($"{TokenIssuer.CookieName}=token-{seen.Store}", seen.Cookie));
        // Round order alternates: the second round opened on the other store.
        var signIns = yard.Seen.Where(seen => seen.Path == "/api/auth/login").Select(seen => seen.Store).ToList();
        Assert.Equal(new[] { "sql", "cosmos", "cosmos", "sql" }, signIns);
        // Fake backends have no store to round-trip to, which the result says by leaving the hop out.
        Assert.All(result.Stores, store => Assert.Null(store.HopMs));
        Assert.False(string.IsNullOrWhiteSpace(result.Sentence));
    }

    /// <summary>
    /// The proof's accounts are made once per process and reused, so a loop of
    /// starts cannot spend the site's hour of registrations (ADR: The one write
    /// a stranger can make, addendum). The second run signs into the accounts
    /// the first one made, registers nothing, and still shows what registering
    /// cost, because the first run's sample is carried into it.
    /// </summary>
    [Fact]
    public async Task A_second_run_signs_into_the_accounts_the_first_one_made_and_registers_nothing()
    {
        var yard = new CannedYard();
        var runner = new ProofRunner(
            new Backends([FakeBackend.Named("sql", "SQLite"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql"),
            new ProofClients(() => new HttpClient(yard) { BaseAddress = new Uri("http://yard.test") }),
            new SqlRingBuffer(10),
            new StoreRingBuffer(10));

        var first = await runner.RunAsync(1);
        int registeredByFirst = yard.Seen.Count(seen => seen.Path == "/api/auth/register");
        var second = await runner.RunAsync(1);

        Assert.Equal("done", first.Status);
        Assert.Equal("done", second.Status);
        Assert.Equal(2, registeredByFirst);
        Assert.Equal(2, yard.Seen.Count(seen => seen.Path == "/api/auth/register"));
        // The second run checked each account and then ran its round: two
        // sign-ins per store where the first run had one.
        Assert.Equal(4, yard.Seen.Count(seen => seen.Path == "/api/auth/login") - 2);
        var register = Assert.Single(second.Rows, row => row.Path == "register");
        Assert.All(register.Cells, cell => Assert.Equal(1, cell.Samples));
    }

    [Fact]
    public async Task A_run_on_a_container_with_one_store_says_it_needs_two()
    {
        var runner = new ProofRunner(
            new Backends([FakeBackend.Named("sql", "SQLite")], "sql"),
            new ProofClients(() => throw new InvalidOperationException("no request should be made")),
            new SqlRingBuffer(10),
            new StoreRingBuffer(10));

        var result = await runner.RunAsync(3);

        Assert.Equal("failed", result.Status);
        Assert.Contains("one store", result.Reason);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task A_registration_that_is_refused_ends_the_run_with_the_status_it_got()
    {
        var refusing = new RefusingYard();
        var runner = new ProofRunner(
            new Backends([FakeBackend.Named("sql", "SQLite"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql"),
            new ProofClients(() => new HttpClient(refusing) { BaseAddress = new Uri("http://yard.test") }),
            new SqlRingBuffer(10),
            new StoreRingBuffer(10));

        var result = await runner.RunAsync(1);

        Assert.Equal("failed", result.Status);
        Assert.Contains("429", result.Reason);
    }

    private sealed class RefusingYard : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    }
    // #endregion canned-run

    // #region proof-endpoints-tests
    /// <summary>
    /// The application with the proof pointed back at its own in-memory
    /// server. A fresh client per call, because the runner disposes the one
    /// it used; the first version handed it the test's own client and the
    /// test's next request found it disposed.
    /// </summary>
    private static (WebApplicationFactory<Program> Factory, HttpClient Client) SelfAddressed(WebApplicationFactory<Program> factory)
    {
        var holder = new FactoryHolder();
        var derived = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton(new ProofClients(() =>
                    (holder.Factory ?? throw new InvalidOperationException("the test server is not there yet"))
                        .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false })))));
        holder.Factory = derived;
        return (derived, derived.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));
    }

    private sealed class FactoryHolder
    {
        public WebApplicationFactory<Program>? Factory { get; set; }
    }

    [Fact]
    public async Task The_proof_runs_in_the_background_and_its_result_is_read_back_or_the_reason_it_could_not_run()
    {
        var (derived, client) = SelfAddressed(factory);
        using (derived)
        {
            var idle = await client.GetFromJsonAsync<JsonElement>("/api/admin/proof");
            Assert.Equal("idle", idle.GetProperty("status").GetString());

            // Starting is a write and takes a signed-in visitor; a stranger is
            // told so and nothing runs (ADR: The one write a stranger can make,
            // addendum). The client keeps no cookies, so the session travels as
            // a header on the requests that need it.
            var stranger = await client.PostAsync("/api/admin/proof?rounds=1", null);
            Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
            Assert.Equal("idle", (await client.GetFromJsonAsync<JsonElement>("/api/admin/proof")).GetProperty("status").GetString());

            string session = await SessionFor(client);
            using var start = new HttpRequestMessage(HttpMethod.Post, "/api/admin/proof?rounds=1");
            start.Headers.Add("Cookie", session);
            var started = await client.SendAsync(start);
            Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

            JsonElement latest = default;
            for (int i = 0; i < 120; i++)
            {
                latest = await client.GetFromJsonAsync<JsonElement>("/api/admin/proof");
                if (latest.GetProperty("status").GetString() != "running")
                {
                    break;
                }
                await Task.Delay(500);
            }

            var stores = await client.GetFromJsonAsync<JsonElement>("/api/stores");
            var result = latest.GetProperty("result");
            if (stores.GetProperty("stores").GetArrayLength() < 2)
            {
                // One store: the proof says it needs two, and says so on the card.
                Assert.Equal("failed", latest.GetProperty("status").GetString());
                Assert.Contains("one store", result.GetProperty("reason").GetString());
            }
            else
            {
                Assert.Equal("done", latest.GetProperty("status").GetString());
                var rows = result.GetProperty("rows").EnumerateArray().ToList();
                Assert.Contains(rows, row => row.GetProperty("path").GetString() == "sign_in"
                    && row.GetProperty("cells").EnumerateArray().All(cell => cell.GetProperty("samples").GetInt32() == 1));
                Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("sentence").GetString()));
            }

            // A second start inside the cooldown is refused with a sentence, not a run.
            using var againRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/proof?rounds=1");
            againRequest.Headers.Add("Cookie", session);
            var again = await client.SendAsync(againRequest);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }
    }

    /// <summary>A fresh account's session cookie, as the browser would carry it.</summary>
    private static async Task<string> SessionFor(HttpClient client)
    {
        var registered = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"proof-test-{Guid.NewGuid():N}@example.com", password = "correct horse battery" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        string cookie = Assert.Single(registered.Headers.GetValues("Set-Cookie"), value => value.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal));
        return cookie.Split(';', 2)[0];
    }
    // #endregion proof-endpoints-tests
}
