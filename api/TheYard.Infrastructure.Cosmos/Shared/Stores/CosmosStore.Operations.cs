// Every call the adapters make to the database goes through here: point reads and writes,
// queries and batches, each timed and written to the store log and the console by the wrapper in
// CosmosOperationLog.cs. A file of its own because this is the part the Admin tab exists to show,
// and the part ADR: What the store is actually doing quotes.
using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

/// <summary>The timed reads and writes of CosmosStore; the type and what it is for are described in CosmosStore.cs.</summary>
public sealed partial class CosmosStore
{
    // #region operations
    /// <summary>A point read that answers null on 404 rather than throwing, because a missing document is an ordinary answer.</summary>
    public async Task<T?> ReadAsync<T>(Container container, string id, string partitionKey, string partitionLabel, CancellationToken cancellation = default) where T : class =>
        (await ReadMeasuredAsync<T>(container, id, partitionKey, partitionLabel, cancellation)).Item;

    /// <summary>The same point read, with what it cost handed back to the caller as well as to the log. The experiment card reads through this.</summary>
    public async Task<MeasuredItem<T>> ReadMeasuredAsync<T>(Container container, string id, string partitionKey, string partitionLabel, CancellationToken cancellation = default) where T : class
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await Recorder.Timed(container, StoreOperationKind.PointRead, "ReadItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
                () => container.ReadItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellation), r => r.Cost());
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
    public Task<ItemResponse<T>> CreateAsync<T>(Container container, T document, string partitionKey, string partitionLabel, string what = "CreateItem", CancellationToken cancellation = default) =>
        Recorder.Timed(container, StoreOperationKind.PointWrite, what, [], partitionLabel, 1,
            () => container.CreateItemAsync(document, new PartitionKey(partitionKey), cancellationToken: cancellation), r => r.Cost());

    /// <summary>A replace that carries the etag it read, so a stale write is refused with 412 rather than winning.</summary>
    public Task<ItemResponse<T>> ReplaceAsync<T>(Container container, T document, string id, string partitionKey, string? etag, string partitionLabel, CancellationToken cancellation = default) =>
        Recorder.Timed(container, StoreOperationKind.PointWrite, "ReplaceItem (If-Match)", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.ReplaceItemAsync(document, id, new PartitionKey(partitionKey), new ItemRequestOptions { IfMatchEtag = etag }, cancellation), r => r.Cost());

    /// <summary>A point delete of one document by id in one partition; a missing document throws the SDK's 404.</summary>
    public Task<ItemResponse<T>> DeleteAsync<T>(Container container, string id, string partitionKey, string partitionLabel, CancellationToken cancellation = default) =>
        Recorder.Timed(container, StoreOperationKind.PointDelete, "DeleteItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey), cancellationToken: cancellation), r => r.Cost());

    /// <summary>
    /// A query, every page of it, with the charge of every page added up and
    /// one line in the store log saying how many pages it took. Pinned to one
    /// partition when the caller can name it, and a fan-out across every
    /// physical partition when it cannot, which is the difference the Admin tab
    /// exists to show (ADR: The partition key).
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel, CancellationToken cancellation = default) =>
        (await QueryMeasuredAsync<T>(container, query, partitionKey, partitionLabel, cancellation)).Items;

    /// <summary>The same query, with the summed charge, the page count and the time handed back as well as logged.</summary>
    public async Task<MeasuredQuery<T>> QueryMeasuredAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel, CancellationToken cancellation = default)
    {
        var parameters = query.GetQueryParameters()
            .Select(p => new SqlParameterShape(p.Name, p.Value?.GetType().Name ?? "null", p.Value is string s ? s.Length : null))
            .ToList();
        var options = new QueryRequestOptions { MaxItemCount = -1 };
        if (partitionKey is not null)
        {
            options.PartitionKey = new PartitionKey(partitionKey);
        }
        int physical = partitionKey is null ? PhysicalPartitionsOf(Recorder.NameOf(container)) : 1;

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
                var page = await iterator.ReadNextAsync(cancellation);
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
            Recorder.Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
            throw;
        }
        Recorder.Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
        return new MeasuredQuery<T>(results, Math.Round(charge, 2), pages, clock.ElapsedMilliseconds);
    }

    /// <summary>One partition's deletes, at most a hundred at a time, as one atomic batch.</summary>
    public async Task<double> DeleteBatchAsync(Container container, string partitionKey, IReadOnlyList<string> ids, string partitionLabel, CancellationToken cancellation = default)
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
            var response = await Recorder.Timed(container, StoreOperationKind.Batch, $"TransactionalBatch: {slice.Count} DeleteItem", [], partitionLabel, 1,
                async () =>
                {
                    // The batch itself runs to its end: it is one atomic unit, and the SDK
                    // cancelling it part way through is the one outcome a batch exists to avoid.
                    var r = await batch.ExecuteAsync();
                    if (!r.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"the batch was refused with {r.StatusCode}");
                    }
                    return r;
                }, r => r.Cost());
            charge += response.RequestCharge;
            cancellation.ThrowIfCancellationRequested();
        }
        return charge;
    }

    /// <summary>
    /// Point reads of many documents in one call, each id with its partition, so the store answers
    /// them as point reads and never as a query that fans out. A document that is not there is
    /// simply absent from the answer.
    /// </summary>
    public async Task<IReadOnlyList<T>> ReadManyAsync<T>(Container container, IReadOnlyList<(string Id, string PartitionKey)> items, string partitionLabel, CancellationToken cancellation = default)
    {
        if (items.Count == 0)
        {
            return [];
        }
        var response = await Recorder.Timed(container, StoreOperationKind.PointRead, $"ReadMany: {items.Count} ReadItem", [], partitionLabel, 1,
            () => container.ReadManyItemsAsync<T>([.. items.Select(item => (item.Id, new PartitionKey(item.PartitionKey)))], cancellationToken: cancellation), r => r.Cost());
        return [.. response];
    }

    /// <summary>One partition's upserts, at most a hundred at a time, each hundred one atomic batch; the summed charge comes back.</summary>
    public async Task<double> UpsertBatchAsync<T>(Container container, string partitionKey, IReadOnlyList<T> documents, string partitionLabel, CancellationToken cancellation = default)
    {
        double charge = 0;
        for (int start = 0; start < documents.Count; start += 100)
        {
            var slice = documents.Skip(start).Take(100).ToList();
            var batch = container.CreateTransactionalBatch(new PartitionKey(partitionKey));
            foreach (var document in slice)
            {
                batch.UpsertItem(document);
            }
            var response = await Recorder.Timed(container, StoreOperationKind.Batch, $"TransactionalBatch: {slice.Count} UpsertItem", [], partitionLabel, 1,
                async () =>
                {
                    // One atomic batch, run to its end for the reason the delete batch gives.
                    var r = await batch.ExecuteAsync();
                    if (!r.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"the batch was refused with {r.StatusCode}");
                    }
                    return r;
                }, r => r.Cost());
            charge += response.RequestCharge;
            // Between batches, not inside one: a fill of a hundred thousand vehicles a hundred
            // at a time is restarted a batch back, so stopping it here loses nothing.
            cancellation.ThrowIfCancellationRequested();
        }
        return charge;
    }

    /// <summary>How many documents a container holds, by a count query across every partition. The seed asks this; the probe does not, because a count is a scan.</summary>
    /// <param name="container">The container to count.</param>
    /// <param name="cancellation">The caller's: a count fans out across every partition, so it is worth stopping.</param>
    internal async Task<int> CountAsync(Container container, CancellationToken cancellation = default)
    {
        var counts = await QueryAsync<int>(container, new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), null, "cross-partition", cancellation);
        return counts.Count == 0 ? 0 : counts[0];
    }

    // #endregion operations
}
