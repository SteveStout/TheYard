using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// Cosmos DB's live source (ADR: Live dials where the source allows, the addendum on Cosmos DB's line): the last five
/// seconds of this process's requests summed into request units a second, nothing older and nothing ahead counted, a
/// throttled request counted as throttled, a quiet five seconds read as zero, and a site with no document store saying so.
/// </summary>
public sealed class CosmosChargeSourceTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private static StoreOperation Operation(double secondsAgo, double charge, string outcome = "200") =>
        new(Nine.AddSeconds(-secondsAgo), "vehicles", StoreOperationKind.Query, "SELECT * FROM c", [], "pinned", 1, charge, 4, outcome, "GET /api/vehicles", null);

    [Fact]
    public async Task The_last_five_seconds_become_request_units_a_second()
    {
        StoreOperation[] ring = [Operation(1, 2.83), Operation(3, 10), Operation(4.9, 7.17), Operation(6, 100), Operation(-1, 50), Operation(2, 1, "failed: TooManyRequests")];
        var source = new CosmosChargeSource(() => ring, () => true);

        var sample = await source.SampleAsync(Nine, CancellationToken.None);

        Assert.Equal(("cosmosdb", 5, "this process's requests, live"), (source.Key, source.GrainSeconds, source.Source));
        Assert.Null(source.Absent);
        Assert.Equal(Nine, sample!.At);
        Assert.Equal(4.2, sample.Values["ru_per_second"]);
        Assert.Equal(4, sample.Values["requests"]);
        Assert.Equal(1, sample.Values["throttled"]);
    }

    [Fact]
    public async Task A_quiet_five_seconds_reads_zero_and_a_site_with_no_document_store_says_so()
    {
        var quiet = await new CosmosChargeSource(() => [Operation(30, 9)], () => true).SampleAsync(Nine, CancellationToken.None);
        var none = new CosmosChargeSource(() => [], () => false);

        Assert.Equal(0, quiet!.Values["ru_per_second"]);
        Assert.Equal(0, quiet.Values["requests"]);
        Assert.Equal("this site keeps no document store", none.Absent);
    }
}
