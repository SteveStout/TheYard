using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// Adapter: the seed catalogue, out of the document store. The same port the
/// JSON file reader and the relational adapter implement, and the synthetic
/// scale-up still wraps it, so nothing above this line can tell which store
/// answered (ADR: A second store on Cosmos DB, and what it costs).
/// </summary>
public sealed class CosmosVehicleSource(CosmosStore store) : IVehicleSource
{
    // #region cosmos-sources
    public async Task<IReadOnlyList<Vehicle>> LoadAsync()
    {
        // The whole container, once, as one cross-partition query, and ordered
        // here rather than by the store. ORDER BY needs an index on the sorted
        // path, and an index on seq would be paid for on every write to save a
        // sort of two hundred documents that costs nothing (ADR: The partition
        // key).
        var documents = await store.QueryAsync<VehicleDocument>(
            store.Vehicles, new QueryDefinition("SELECT * FROM c"), partitionKey: null, "cross-partition");
        return documents.OrderBy(d => d.Seq).Select(d => d.ToVehicle()).ToList();
    }
    // #endregion cosmos-sources
}

/// <summary>Adapter: the photo manifest, out of the document store.</summary>
public sealed class CosmosPhotoManifestSource(CosmosStore store) : IPhotoManifestSource
{
    public async Task<IReadOnlyList<PhotoEntry>> LoadAsync()
    {
        var documents = await store.QueryAsync<PhotoDocument>(
            store.Photos, new QueryDefinition("SELECT * FROM c"), partitionKey: null, "cross-partition");
        return documents.OrderBy(d => d.Seq).Select(d => d.ToEntry()).ToList();
    }
}

/// <summary>
/// Adapter: bids, in a container partitioned on the buyer. Every operation
/// here names the partition, so every one of them is a point operation or a
/// query inside one partition; the only fan-out is the read at startup
/// (ADR: The partition key).
/// </summary>
public sealed class CosmosBidStore(CosmosStore store) : IBidStore
{
    private const string Partition = "pinned to the buyer";

    // #region cosmos-bid-store
    public async Task<IReadOnlyList<StoredBid>> LoadAsync()
    {
        var documents = await store.QueryAsync<BidDocument>(
            store.Bids, new QueryDefinition("SELECT * FROM c"), partitionKey: null, "cross-partition");
        return documents.Select(d => d.ToStoredBid()).ToList();
    }

    /// <summary>
    /// One document per buyer per vehicle, replaced rather than appended. A
    /// point read, then a create if there was nothing or a replace carrying the
    /// etag the read returned. Three tries, then the exception travels: a 412
    /// means another writer moved this buyer's document between the read and
    /// the write, a 409 means another writer created it first, and both take
    /// the one account bidding from two containers at once. The retry reads
    /// again and writes this bid over what it finds, the same as the relational
    /// store (ADR: The SQL Server backend): the rules ran on this side already,
    /// and what the document held in between is not re-examined. Two buyers
    /// racing each other are two documents and never meet here; that race is
    /// the gap the review record names (ADR: Three readers with no memory of
    /// the project).
    /// </summary>
    public async Task SaveAsync(string userId, string vehicleId, BidState state)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await WriteAsync(userId, vehicleId, state);
                return;
            }
            catch (CosmosException ex) when (attempt < 3
                && ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
                // Deliberately empty. The retry is the handling.
            }
        }
    }

    private async Task WriteAsync(string userId, string vehicleId, BidState state)
    {
        var existing = await store.ReadAsync<BidDocument>(store.Bids, vehicleId, userId, Partition);
        var document = new BidDocument
        {
            Id = vehicleId,
            UserId = userId,
            Amount = state.Amount,
            BidCount = state.BidCount,
            WonBuyNow = state.WonBuyNow,
            AtMs = state.AtMs,
        };
        if (existing is null)
        {
            await store.CreateAsync(store.Bids, document, userId, Partition);
        }
        else
        {
            await store.ReplaceAsync(store.Bids, document, vehicleId, userId, existing.ETag, Partition);
        }
    }

    /// <summary>
    /// One person's documents: a query inside that person's partition for the
    /// ids, then the deletes as an atomic batch. There is no ExecuteDelete on
    /// this side; the batch is what a partition offers instead
    /// (ADR: Reset is one person's start-over).
    /// </summary>
    public async Task ClearAsync(string userId)
    {
        var ids = await store.QueryAsync<string>(
            store.Bids,
            new QueryDefinition("SELECT VALUE c.id FROM c WHERE c.user_id = @user").WithParameter("@user", userId),
            partitionKey: userId,
            Partition);
        if (ids.Count > 0)
        {
            await store.DeleteBatchAsync(store.Bids, userId, ids, Partition);
        }
    }
    // #endregion cosmos-bid-store
}
