// Every call the adapters make to the database goes through here: point reads and writes,
// queries, batches, and the wrapper that times each one and writes its charge to the store log
// and the console. A file of its own because this is the part the Admin tab exists to show,
// and the part ADR: What the store is actually doing quotes.
using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>The timed reads and writes of CosmosStore; the type and what it is for are described in CosmosStore.cs.</summary>
public sealed partial class CosmosStore
{
    // #region operations
    /// <summary>A point read that answers null on 404 rather than throwing, because a missing document is an ordinary answer.</summary>
    public async Task<T?> ReadAsync<T>(Container container, string id, string partitionKey, string partitionLabel) where T : class =>
        (await ReadMeasuredAsync<T>(container, id, partitionKey, partitionLabel)).Item;

    /// <summary>The same point read, with what it cost handed back to the caller as well as to the log. The experiment card reads through this.</summary>
    public async Task<MeasuredItem<T>> ReadMeasuredAsync<T>(Container container, string id, string partitionKey, string partitionLabel) where T : class
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await Timed(container, StoreOperationKind.PointRead, "ReadItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
                () => container.ReadItemAsync<T>(id, new PartitionKey(partitionKey)), r => r.Cost());
            return new MeasuredItem<T>(response.Resource, Math.Round(response.RequestCharge, 2), clock.ElapsedMilliseconds);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new MeasuredItem<T>(null, Math.Round(ex.RequestCharge, 2), clock.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// A point create in one partition. A second create of an id that is
    /// already there is refused with 409, which is what the email claim relies
    /// on. <paramref name="what"/> is the text the store log shows for it.
    /// </summary>
    public Task<ItemResponse<T>> CreateAsync<T>(Container container, T document, string partitionKey, string partitionLabel, string what = "CreateItem") =>
        Timed(container, StoreOperationKind.PointWrite, what, [], partitionLabel, 1,
            () => container.CreateItemAsync(document, new PartitionKey(partitionKey)), r => r.Cost());

    /// <summary>A replace that carries the etag it read, so a stale write is refused with 412 rather than winning.</summary>
    public Task<ItemResponse<T>> ReplaceAsync<T>(Container container, T document, string id, string partitionKey, string? etag, string partitionLabel) =>
        Timed(container, StoreOperationKind.PointWrite, "ReplaceItem (If-Match)", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.ReplaceItemAsync(document, id, new PartitionKey(partitionKey), new ItemRequestOptions { IfMatchEtag = etag }), r => r.Cost());

    /// <summary>A point delete of one document by id in one partition; a missing document throws the SDK's 404.</summary>
    public Task<ItemResponse<T>> DeleteAsync<T>(Container container, string id, string partitionKey, string partitionLabel) =>
        Timed(container, StoreOperationKind.PointDelete, "DeleteItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey)), r => r.Cost());

    /// <summary>
    /// A query, every page of it, with the charge of every page added up and
    /// one line in the store log saying how many pages it took. Pinned to one
    /// partition when the caller can name it, and a fan-out across every
    /// physical partition when it cannot, which is the difference the Admin tab
    /// exists to show (ADR: The partition key).
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel) =>
        (await QueryMeasuredAsync<T>(container, query, partitionKey, partitionLabel)).Items;

    /// <summary>The same query, with the summed charge, the page count and the time handed back as well as logged.</summary>
    public async Task<MeasuredQuery<T>> QueryMeasuredAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel)
    {
        var parameters = query.GetQueryParameters()
            .Select(p => new SqlParameterShape(p.Name, p.Value?.GetType().Name ?? "null", p.Value is string s ? s.Length : null))
            .ToList();
        var options = new QueryRequestOptions { MaxItemCount = -1 };
        if (partitionKey is not null)
        {
            options.PartitionKey = new PartitionKey(partitionKey);
        }
        int physical = partitionKey is null ? PhysicalPartitionsOf(NameOf(container)) : 1;

        var results = new List<T>();
        double charge = 0;
        int pages = 0;
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                charge += page.RequestCharge;
                pages++;
                results.AddRange(page);
            }
            outcome = $"{results.Count} document(s) in {pages} page(s)";
        }
        catch (CosmosException ex)
        {
            charge += ex.RequestCharge;
            outcome = "failed: " + ex.StatusCode;
            Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
            throw;
        }
        Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
        return new MeasuredQuery<T>(results, Math.Round(charge, 2), pages, clock.ElapsedMilliseconds);
    }

    /// <summary>One partition's deletes, at most a hundred at a time, as one atomic batch.</summary>
    public async Task<double> DeleteBatchAsync(Container container, string partitionKey, IReadOnlyList<string> ids, string partitionLabel)
    {
        double charge = 0;
        for (int start = 0; start < ids.Count; start += 100)
        {
            var slice = ids.Skip(start).Take(100).ToList();
            var batch = container.CreateTransactionalBatch(new PartitionKey(partitionKey));
            foreach (string id in slice)
            {
                batch.DeleteItem(id);
            }
            var response = await Timed(container, StoreOperationKind.Batch, $"TransactionalBatch: {slice.Count} DeleteItem", [], partitionLabel, 1,
                async () =>
                {
                    var r = await batch.ExecuteAsync();
                    if (!r.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"the batch was refused with {r.StatusCode}");
                    }
                    return r;
                }, r => r.Cost());
            charge += response.RequestCharge;
        }
        return charge;
    }

    /// <summary>How many documents a container holds, by a count query across every partition. The seed asks this; the probe does not, because a count is a scan.</summary>
    private async Task<int> CountAsync(Container container)
    {
        var counts = await QueryAsync<int>(container, new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), null, "cross-partition");
        return counts.Count == 0 ? 0 : counts[0];
    }

    /// <summary>
    /// Run one operation and write its charge and its duration to the store
    /// log, whether it succeeded or not. The store log is a public page: this
    /// records the kind, the container, the shape of the parameters and the
    /// charge, and never an id that could be an address or a value that could
    /// be anything (ADR: What the store is actually doing).
    /// </summary>
    private async Task<TResponse> Timed<TResponse>(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, Func<Task<TResponse>> operation, Func<TResponse, Cost> cost)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await operation();
            var paid = cost(response);
            Record(container, kind, text, parameters, partitionLabel, physical, paid.Charge, clock.Elapsed, paid.Status.ToString());
            return response;
        }
        catch (CosmosException ex)
        {
            Record(container, kind, text, parameters, partitionLabel, physical, ex.RequestCharge, clock.Elapsed, "failed: " + (int)ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            // A refused batch throws its own exception rather than the SDK's,
            // and it is recorded here too, so the one operation that failed is
            // never the one operation the page cannot show.
            Record(container, kind, text, parameters, partitionLabel, physical, 0, clock.Elapsed, "failed: " + ex.GetType().Name);
            throw;
        }
    }

    // #region record
    /// <summary>
    /// Write one finished operation to the store log and as one console line:
    /// the kind, the container, the charge, the time, the partition described,
    /// the outcome and the query text, and never a parameter's value.
    /// </summary>
    private void Record(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, double charge, TimeSpan elapsed, string outcome)
    {
        // Nothing in here may break the operation it observed
        // (ADR: What the database is actually doing).
        try
        {
            string name = NameOf(container);
            double rounded = Math.Round(charge, 2);
            long ms = (long)elapsed.TotalMilliseconds;
            _log.Record(new StoreOperation(
                DateTimeOffset.UtcNow,
                name,
                kind,
                text,
                parameters,
                partitionLabel,
                physical,
                rounded,
                ms,
                outcome,
                CurrentRequest.Describe(),
                CurrentRequest.Identify()));
            // The same operation as one console line, the shape Entity
            // Framework gives a statement: what ran, what it cost, how long,
            // and the query with its parameters by name and never by value.
            Logger.LogInformation(
                "Executed Cosmos DB {Kind} on {Container} ({Charge} RU, {Ms} ms, {Partition}) {Outcome}: {Text}",
                kind, name, rounded, ms, partitionLabel, outcome, text);
        }
        catch
        {
            // Deliberately silent: a failure to record must never become a
            // failure of the operation, the same reason the SQL interceptor gives.
        }
    }
    // #endregion record
    // #endregion operations
}
