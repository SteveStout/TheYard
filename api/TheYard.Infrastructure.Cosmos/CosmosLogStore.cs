using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// The kept log in the document store (ADR: Logs that outlive the container).
/// One container partitioned on the UTC day, one document per event, and the
/// container's own time-to-live as the retention policy: a document expires a
/// year after it is written and nothing has to run to delete it.
///
/// <para>Why this store and not the relational one. A log is append-only,
/// written constantly and read rarely, and its three kinds of line have three
/// shapes; a document takes each shape as it is, the write costs a few request
/// units, and a writer that runs every minute never keeps a database awake,
/// which on the relational side's serverless offer it would. The relational
/// side keeps the counters the graph is drawn from, which is the query that
/// wants a table.</para>
///
/// <para>Like the activity adapter, this one calls the container directly
/// rather than through the measured helpers in <see cref="CosmosStore"/>: a
/// batch of log writes must not appear in the store log it is partly made of.
/// What it costs is counted here and reported with the card.</para>
/// </summary>
public sealed class CosmosLogStore(CosmosStore store) : ILogStore
{
    /// <summary>A transactional batch takes up to a hundred operations; one batch per day partition per hundred events.</summary>
    private const int BatchSize = 100;

    private readonly Container _container = store.ContainerNamed(Containers.Logs);
    private LogAvailability? _availability;
    private double _charge;
    private int _operations;
    private int _failures;

    /// <summary>What this feature has cost the store since the process started: request units, operations, failures.</summary>
    public (double Charge, int Operations, int Failures) Cost => (Math.Round(_charge, 2), _operations, _failures);

    // #region availability
    public async Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }

        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            Count(response.RequestCharge);
            string key = response.Resource.PartitionKeyPath;
            int? ttl = response.Resource.DefaultTimeToLive;
            return _availability = key == Containers.PartitionKeyPaths[Containers.Logs]
                ? new LogAvailability(true, ttl is > 0 ? $"kept in Azure Cosmos DB for {ttl.Value / 86_400} days" : "kept in Azure Cosmos DB with no expiry")
                : new LogAvailability(false, $"the logs container is partitioned on {key} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new LogAvailability(false, "the logs container is not there; apply infra/cosmos/logs.json");
        }
        catch (Exception ex)
        {
            return _availability = new LogAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }
    // #endregion availability

    // #region append
    public async Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation)
    {
        if (events.Count == 0 || !(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }

        foreach (var day in events.GroupBy(e => ActivityFolding.DayOf(e.At), StringComparer.Ordinal))
        {
            var partition = new PartitionKey(day.Key);
            foreach (var chunk in day.Chunk(BatchSize))
            {
                var batch = _container.CreateTransactionalBatch(partition);
                foreach (var e in chunk)
                {
                    batch.CreateItem(LogDocument.From(e, day.Key));
                }

                try
                {
                    using var response = await batch.ExecuteAsync(cancellation);
                    Count(response.RequestCharge);
                    if (!response.IsSuccessStatusCode)
                    {
                        _failures++;
                    }
                }
                catch (CosmosException ex)
                {
                    Count(ex.RequestCharge);
                    _failures++;
                }
            }
        }
    }
    // #endregion append

    // #region query
    public async Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        // The day narrows the partitions the query fans out to; the timestamp
        // narrows within the first day. Everything after is optional and
        // parameterised, so a fragment of a path is a value and never syntax.
        string text = "SELECT TOP @take * FROM c WHERE c.day >= @day AND c.at >= @at";
        var definition = new QueryDefinition(text)
            .WithParameter("@take", Math.Clamp(query.Take, 1, 1_000))
            .WithParameter("@day", ActivityFolding.DayOf(query.Since))
            .WithParameter("@at", query.Since.ToUniversalTime().ToString("O"));
        if (!string.IsNullOrEmpty(query.Kind))
        {
            text += " AND c.kind = @kind";
            definition = definition.WithParameter("@kind", query.Kind);
        }
        if (query.Status is int status)
        {
            text += " AND c.status = @status";
            definition = definition.WithParameter("@status", status);
        }
        if (!string.IsNullOrEmpty(query.PathContains))
        {
            text += " AND CONTAINS(c.path, @path)";
            definition = definition.WithParameter("@path", query.PathContains);
        }
        text += " ORDER BY c.at DESC";
        definition = Rebuild(definition, text);

        var documents = new List<LogDocument>();
        using var feed = _container.GetItemQueryIterator<LogDocument>(definition, requestOptions: new QueryRequestOptions { MaxItemCount = Math.Clamp(query.Take, 1, 1_000) });
        while (feed.HasMoreResults && documents.Count < query.Take)
        {
            var page = await feed.ReadNextAsync(cancellation);
            Count(page.RequestCharge);
            documents.AddRange(page);
        }

        return documents.Take(query.Take).Select(d => d.ToEvent()).ToList();
    }

    public async Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        var definition = new QueryDefinition("SELECT c.kind, COUNT(1) AS count FROM c WHERE c.day >= @day AND c.at >= @at GROUP BY c.kind")
            .WithParameter("@day", ActivityFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O"));
        var counts = new List<LogCount>();
        using var feed = _container.GetItemQueryIterator<LogCountRow>(definition);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellation);
            Count(page.RequestCharge);
            counts.AddRange(page.Select(row => new LogCount(row.Kind, row.Count)));
        }

        return counts.OrderBy(count => count.Kind, StringComparer.Ordinal).ToList();
    }

    /// <summary>The SDK's definition is immutable in its text; the parameters are carried over onto the final text.</summary>
    private static QueryDefinition Rebuild(QueryDefinition built, string text)
    {
        var rebuilt = new QueryDefinition(text);
        foreach (var (name, value) in built.GetQueryParameters())
        {
            rebuilt = rebuilt.WithParameter(name, value);
        }

        return rebuilt;
    }

    private sealed class LogCountRow
    {
        public string Kind { get; set; } = "";
        public int Count { get; set; }
    }
    // #endregion query

    private void Count(double charge)
    {
        _charge += charge;
        _operations++;
    }
}
