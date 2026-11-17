using System.Net;
using Microsoft.Azure.Cosmos;

namespace TheYard.Tests;

// #region cosmos-same-answer
/// <summary>
/// The document store's catalogue held to the in-memory one, the way StoreCatalogueTests holds the
/// relational adapter (ADR: The store does the work). The filter is written three times, as
/// VehicleFilter, as an EF Core query and as Cosmos DB SQL, and this is what holds the third to the
/// first: over the same expansion, the same three counts and the same rows in the same order for every
/// filter, order and page the relational test tries, at the same three instants, and once the walks
/// have kept their places, every deep page they serve again. It reads and writes the live tests' own
/// copy of the catalogue, tests-catalogue, never the site's, and fills it through the port only when
/// the day's documents have expired.
/// </summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public sealed class CosmosCatalogueTests
{
    /// <summary>Half the relational test's twelve hundred: enough for three kept places an order, at a few thousand request units a fill.</summary>
    private const int Target = 600;

    /// <summary>The pages asked in every order: the first, one inside it, and one deep enough that a kept place serves it.</summary>
    private static readonly (int Skip, int Take)[] Pages = [(0, 24), (30, 7), (550, 100)];

    [Fact]
    public async Task The_document_store_answers_every_filter_and_order_as_the_list_does()
    {
        var store = await CosmosLive.Prepared();
        await TheTestCatalogueIsThereAsync(store);
        var vehicles = await StoreCatalogueTests.ExpansionAsync(Target);
        var catalogue = new CosmosVehicleCatalogue(store, Target);
        var memory = new MemoryCatalogue(vehicles);
        if (await catalogue.FilledAsync(CancellationToken.None) < Target)
        {
            // A transactional batch is one make's, and holds at most a hundred.
            for (int start = 0; start < Target; start += 100)
            {
                await catalogue.FillAsync([.. vehicles.Skip(start).Take(100).Select((v, i) => new CatalogueEntry(start + i, v))], CancellationToken.None);
            }
        }
        Assert.Equal(Target, await catalogue.FilledAsync(CancellationToken.None));

        var facets = await memory.FacetsAsync(CancellationToken.None);
        Assert.Equal(facets.Makes, (await catalogue.FacetsAsync(CancellationToken.None)).Makes);
        var filters = StoreCatalogueTests.Filters(vehicles, facets);
        int compared = 0;
        foreach (long since in StoreCatalogueTests.Instants)
        {
            foreach (var shape in filters)
            {
                var filter = shape with { SinceAnchorMs = since };
                Assert.Equal(await memory.CountAsync(filter, CancellationToken.None), await catalogue.CountAsync(filter, CancellationToken.None));
                foreach (var order in Enum.GetValues<CatalogueOrder>())
                {
                    foreach (var (skip, take) in Pages)
                    {
                        await SamePageAsync(memory, catalogue, filter, order, skip, take);
                        compared++;
                    }
                }
            }
        }
        Assert.True(compared > 600);

        // Every deep page a kept place serves, twice: the first time with OFFSET, which starts the
        // walks, and again once the walks have kept their places, which must be the same rows.
        var kept = new (CatalogueSegment Segment, CatalogueOrder Order)[]
        {
            (CatalogueSegment.Live, CatalogueOrder.EndAscending),
            (CatalogueSegment.Upcoming, CatalogueOrder.StartAscending),
            (CatalogueSegment.Ended, CatalogueOrder.EndDescending),
        };
        async Task KeptPagesAsync()
        {
            foreach (long since in StoreCatalogueTests.Instants)
            {
                foreach (var order in Enum.GetValues<CatalogueOrder>())
                {
                    await SamePageAsync(memory, catalogue, new CatalogueFilter { SinceAnchorMs = since }, order, 260, 24);
                    await SamePageAsync(memory, catalogue, new CatalogueFilter { SinceAnchorMs = since }, order, 550, 100);
                }
                foreach (var (segment, order) in kept)
                {
                    await SamePageAsync(memory, catalogue, new CatalogueFilter { SinceAnchorMs = since, Segment = segment }, order, 260, 24);
                }
            }
        }
        await KeptPagesAsync();
        await catalogue.Walking;
        if (catalogue.LiveWalking is { } live)
        {
            await live;
        }
        await KeptPagesAsync();

        var found = await catalogue.FindAsync([vehicles[7].Id, vehicles[500].Id, "nope"], CancellationToken.None);
        Assert.Equal([(7, vehicles[7].Id), (500, vehicles[500].Id)], found.Select(entry => (entry.Seq, entry.Vehicle.Id)));
    }
    // #endregion cosmos-same-answer

    /// <summary>One page from both, compared by place and id, in order.</summary>
    private static async Task SamePageAsync(MemoryCatalogue memory, CosmosVehicleCatalogue catalogue, CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        var expected = (await memory.PageAsync(filter, order, skip, take, CancellationToken.None)).Select(e => (e.Seq, e.Vehicle.Id)).ToList();
        var actual = (await catalogue.PageAsync(filter, order, skip, take, CancellationToken.None)).Select(e => (e.Seq, e.Vehicle.Id)).ToList();
        Assert.True(expected.SequenceEqual(actual), $"{order} {skip}/{take} at {filter.SinceAnchorMs} for {filter.Key()}");
    }

    /// <summary>
    /// The live tests' copy of the catalogue, which a person makes: the code creates no container. Its
    /// absence fails here with what to do, not on the first query with a bare 404.
    /// </summary>
    private static async Task TheTestCatalogueIsThereAsync(CosmosStore store)
    {
        var container = store.ContainerNamed(Containers.Catalogue);
        try
        {
            var response = await container.ReadContainerAsync();
            Assert.Equal("/make", response.Resource.PartitionKeyPath);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Fail(
                $"{container.Id} is not on the account. It is the live tests' copy of the catalogue: infra/cosmos/catalogue.json's "
                + "partition key and indexing policy under the tests- prefix, with a day's time to live. The database's shared "
                + "throughput holds at most 25 containers (CosmosDefinitionTests), so it may need one retired first.");
        }
    }
}
