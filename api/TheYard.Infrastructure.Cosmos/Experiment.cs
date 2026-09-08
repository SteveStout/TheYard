using System.Net;
using Microsoft.Azure.Cosmos;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>One query of the experiment: what it asked, how far it fanned out, and what it cost.</summary>
public sealed record ExperimentRow(string Query, string Partitions, double RequestCharge, long DurationMs, int Documents);

/// <summary>The experiment card's answer, including the four ways it can have nothing to show.</summary>
public sealed record ExperimentResult(
    bool Available,
    string? Reason,
    string Container,
    int PhysicalPartitions,
    int Documents,
    IReadOnlyList<ExperimentRow> Rows,
    DateTimeOffset RanAt);

/// <summary>
/// The partition key, live (ADR: The partition key). The same seven queries
/// the console tool measures, run once against the 100,000-document catalogue
/// with this container's own identity and shown on the Admin tab with the
/// request charge beside each, so the difference between a query that names
/// the key and one that cannot is a number on a page rather than a claim.
///
/// <para>Cached for a minute. An open Admin tab asks every thirty seconds, and
/// the set costs on the order of a hundred request units; a minute's cache
/// halves that and changes nothing a reader would notice.</para>
/// </summary>
public static class Experiment
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static ExperimentResult? _last;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // #region experiment
    public static async Task<ExperimentResult> RunAsync(CosmosStore store)
    {
        var cached = _last;
        if (cached is not null && DateTimeOffset.UtcNow - cached.RanAt < CacheFor)
        {
            return cached;
        }

        await Gate.WaitAsync();
        try
        {
            cached = _last;
            if (cached is not null && DateTimeOffset.UtcNow - cached.RanAt < CacheFor)
            {
                return cached;
            }
            var result = await MeasureAsync(store);
            _last = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<ExperimentResult> MeasureAsync(CosmosStore store)
    {
        var container = store.ContainerNamed(Containers.Catalogue);
        int physical;
        try
        {
            physical = (await container.GetFeedRangesAsync()).Count;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ExperimentResult(false, "the catalogue container does not exist on this account; apply infra/cosmos/catalogue.json and seed it with the experiment tool", Containers.Catalogue, 0, 0, [], DateTimeOffset.UtcNow);
        }

        var count = await store.QueryMeasuredAsync<int>(container, new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), null, "cross-partition");
        int documents = count.Items.Count == 0 ? 0 : count.Items[0];
        if (documents == 0)
        {
            return new ExperimentResult(false, "the catalogue container is empty; seed it with the experiment tool (about ten minutes at 1000 RU/s)", Containers.Catalogue, physical, 0, [], DateTimeOffset.UtcNow);
        }

        var sample = await store.QueryMeasuredAsync<VehicleDocument>(container, new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.make = 'Ford'"), "Ford", "pinned to the make");
        string id = sample.Items.Count == 0 ? "" : sample.Items[0].Id;
        string all = $"all ({physical} physical)";
        var rows = new List<ExperimentRow>();

        if (id.Length > 0)
        {
            var point = await store.ReadMeasuredAsync<VehicleDocument>(container, id, "Ford", "pinned to the make");
            rows.Add(new ExperimentRow("point read, id and make known", "1 logical", point.Charge, point.DurationMs, point.Item is null ? 0 : 1));
        }

        var queries = new (string Name, string? Partition, QueryDefinition Query)[]
        {
            ("make = Ford, by price, page of 100", "Ford", new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.make = @make ORDER BY c.starting_bid").WithParameter("@make", "Ford")),
            ("province = Ontario, by price, page of 100", null, new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.province = @province ORDER BY c.starting_bid").WithParameter("@province", "Ontario")),
            ("no filter, by price, page of 100", null, new QueryDefinition("SELECT TOP 100 * FROM c ORDER BY c.starting_bid")),
            ("SUV, clean title, grade 4 or better, page of 100", null, new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.body_style = 'SUV' AND c.title_status = 'clean' AND c.condition_grade >= 4")),
            ("id only, make unknown", null, new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id)),
            ("free text, CONTAINS on the model", null, new QueryDefinition("SELECT TOP 100 * FROM c WHERE CONTAINS(LOWER(c.model), 'cx-5')")),
        };
        foreach (var (name, partition, query) in queries)
        {
            var measured = await store.QueryMeasuredAsync<VehicleDocument>(container, query, partition, partition is null ? "cross-partition" : "pinned to the make");
            rows.Add(new ExperimentRow(name, partition is null ? all : "1 logical", measured.Charge, measured.DurationMs, measured.Items.Count));
        }
        var counted = await store.QueryMeasuredAsync<int>(container, new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.make = @make").WithParameter("@make", "Ford"), "Ford", "pinned to the make");
        rows.Add(new ExperimentRow("count of Ford", "1 logical", counted.Charge, counted.DurationMs, counted.Items.Count == 0 ? 0 : counted.Items[0]));

        return new ExperimentResult(true, null, Containers.Catalogue, physical, documents, rows, DateTimeOffset.UtcNow);
    }
    // #endregion experiment
}
