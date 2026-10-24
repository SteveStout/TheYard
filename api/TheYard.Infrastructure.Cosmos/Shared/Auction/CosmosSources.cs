using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.Auction;
using TheYard.Data;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Auction;

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
/// Adapter: bids, in a container partitioned on the buyer, and each vehicle's
/// standing in the same container under a partition of its own. Every operation
/// here names the partition, so every one of them is a point operation or a
/// query inside one partition; the fan-outs are the read at startup and the
/// rows on one vehicle that has no standing yet (ADR: The partition key). The
/// standing half is in CosmosBidStore.Standing.cs.
/// </summary>
public sealed partial class CosmosBidStore(CosmosStore store) : IBidStore
{
    private const string Partition = "pinned to the buyer";

    // #region cosmos-bid-store
    /// <summary>Every buyer's document, and none of the standings that share the container.</summary>
    public async Task<IReadOnlyList<StoredBid>> LoadAsync()
    {
        var documents = await store.QueryAsync<BidDocument>(
            store.Bids,
            new QueryDefinition("SELECT * FROM c WHERE c.user_id != @standing").WithParameter("@standing", StandingDocument.Partition),
            partitionKey: null,
            "cross-partition");
        return documents.Select(d => d.ToStoredBid()).ToList();
    }

    /// <summary>
    /// Every buyer's document on one vehicle: the id is the vehicle, so this is
    /// one id across every buyer's partition. It runs only when the vehicle has
    /// no standing yet.
    /// </summary>
    public async Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId)
    {
        var documents = await store.QueryAsync<BidDocument>(
            store.Bids,
            new QueryDefinition("SELECT * FROM c WHERE c.id = @vehicle AND c.user_id != @standing")
                .WithParameter("@vehicle", vehicleId)
                .WithParameter("@standing", StandingDocument.Partition),
            partitionKey: null,
            "cross-partition");
        return documents.Select(d => d.ToStoredBid()).ToList();
    }

    /// <summary>
    /// The standing first, with a compare-and-set on its etag, then the buyer's
    /// document. They live in different partitions, so this is two writes and
    /// not a transaction: the standing goes first because it is what the rules
    /// read, and a buyer's document missing after a standing that names them is
    /// repaired from the standing when the cache is next read from the store.
    /// A 412 or 409 on the standing answers false, nothing written.
    /// </summary>
    public async Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion)
    {
        if (!await TryWriteStandingAsync(vehicleId, standing, expectedVersion))
        {
            return false;
        }

        await SaveBuyerAsync(userId, vehicleId, state);
        return true;
    }

    /// <summary>
    /// One document per buyer per vehicle, replaced rather than appended. A
    /// point read, then a create if there was nothing or a replace carrying the
    /// etag the read returned. Three tries, then the exception travels: a 412
    /// means another writer moved this buyer's document between the read and
    /// the write, a 409 means another writer created it first, and both take
    /// the one account bidding from two sites at once. The standing has
    /// already been decided by then, so the retry writes this bid over what it
    /// finds.
    /// </summary>
    private async Task SaveBuyerAsync(string userId, string vehicleId, BidState state)
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
    /// ids, then the deletes as an atomic batch, then the standing of each of
    /// those vehicles, so the next bid works it out again from whoever is left.
    /// There is no ExecuteDelete on this side; the batch is what a partition
    /// offers instead (ADR: Reset is one person's start-over).
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
            await ForgetStandingsAsync(ids);
        }
    }
    // #endregion cosmos-bid-store
}
