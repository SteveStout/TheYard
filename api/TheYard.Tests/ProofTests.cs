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

            var started = await client.PostAsync("/api/admin/proof?rounds=1", null);
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
            var again = await client.PostAsync("/api/admin/proof?rounds=1", null);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }
    }
    // #endregion proof-endpoints-tests
}
