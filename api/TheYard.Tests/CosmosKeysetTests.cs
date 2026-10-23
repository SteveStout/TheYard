using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Tests;

/// <summary>
/// The keyset read on the document store, against the site's own catalogue container, read only: a
/// page deep in the live segment from the kept places is the page OFFSET reads, row for row, at a
/// small fraction of its request units (ADR: The store does the work, the addendum of 19 October).
/// It reads the site's container because the test containers hold no catalogue, and a walk and
/// three pages cost about two thousand request units, a few seconds of the account's thousand a second.
/// </summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public class CosmosKeysetTests
{
    [Fact]
    public async Task A_deep_live_page_from_the_kept_places_is_the_offset_page_for_under_a_hundred_request_units()
    {
        var log = new StoreRingBuffer(500);
        var store = CosmosStore.Connect(
            CosmosLive.Endpoint,
            Environment.GetEnvironmentVariable("Cosmos__Database") ?? "theyard",
            "",
            Environment.GetEnvironmentVariable("Cosmos__Credential") ?? "azure-cli",
            Environment.GetEnvironmentVariable("Azure__ClientId") ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
            log);
        var catalogue = new CosmosVehicleCatalogue(store, 100_000);
        long walkedAt = (long)(DateTimeOffset.UtcNow - DateTimeOffset.UtcNow.UtcDateTime.Date).TotalMilliseconds;
        // An hour later the same morning, or earlier when the walk is late in the day: the places
        // are then corrected by the rows that ended and started in between.
        long later = walkedAt < 20 * 3_600_000L ? walkedAt + 3_600_000L / 2 : walkedAt;
        var live = new CatalogueFilter { SinceAnchorMs = walkedAt, Segment = CatalogueSegment.Live };
        var liveLater = live with { SinceAnchorMs = later };

        // The first deep page reads with OFFSET and starts the walk of the live set.
        var byOffset = await catalogue.PageAsync(live, CatalogueOrder.EndAscending, 5_000, 100);
        Assert.NotNull(catalogue.LiveWalking);
        await catalogue.LiveWalking!;

        foreach (var (filter, expected) in new[] { (live, byOffset), (liveLater, await OffsetPageAsync(store, liveLater)) })
        {
            var since = DateTimeOffset.UtcNow;
            var byKey = await catalogue.PageAsync(filter, CatalogueOrder.EndAscending, 5_000, 100);
            var operations = log.Snapshot().Where(operation => operation.At >= since).ToList();

            Assert.Equal(100, byKey.Count);
            Assert.Equal(expected.Select(entry => entry.Seq), byKey.Select(entry => entry.Seq));
            Assert.DoesNotContain(operations, operation => operation.Text.Contains("OFFSET 5000", StringComparison.Ordinal));
            double charge = operations.Sum(operation => operation.RequestCharge);
            Assert.True(charge < 100, $"the kept-place page cost {charge} request units: {string.Join(" | ", operations.Select(operation => $"{operation.RequestCharge} {operation.Text}"))}");
        }
    }

    /// <summary>The same page read the old way, with OFFSET, written out here so nothing keeps places for it.</summary>
    private static async Task<IReadOnlyList<CatalogueEntry>> OffsetPageAsync(CosmosStore store, CatalogueFilter filter)
    {
        var documents = await store.QueryAsync<CatalogueDocument>(
            store.ContainerNamed(Containers.Catalogue),
            new Microsoft.Azure.Cosmos.QueryDefinition(
                    "SELECT * FROM c WHERE c.seq < @target AND c.price >= 0 AND (c.start_offset <= @d AND c.end_offset > @d) ORDER BY c.end_offset ASC, c.seq ASC OFFSET 5000 LIMIT 100")
                .WithParameter("@target", 100_000)
                .WithParameter("@d", filter.SinceAnchorMs),
            partitionKey: null,
            "cross-partition");
        return [.. documents.Select(document => document.ToEntry())];
    }
}
