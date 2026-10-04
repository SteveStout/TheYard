// The three ports, answered from the database: the seed catalogue, the photo manifest, and the
// bids. These are the adapters the composition root wires when the relational store opens.
// The rest of the relational plumbing has a file of its own beside this one:
//   VehicleRows.cs     row to domain and back
//   DatabaseState.cs   whether the store is usable, and one sentence about why
//   YardDatabase.cs    bringing the database up once at startup
//   SeedResult.cs      what the first boot found
//   YardSeed.cs        filling an empty catalogue from the JSON files
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure;

/// <summary>
/// Adapter: the seed catalogue, out of the database. Same port the JSON file
/// reader implements, and the synthetic scale-up still wraps it, so nothing
/// above this layer depends on which storage is underneath.
/// </summary>
public sealed class EfVehicleSource(IDbContextFactory<YardDbContext> factory) : IVehicleSource
{
    // #region ef-sources
    /// <summary>Every seed vehicle, in the order it was seeded, read once without change tracking.</summary>
    public async Task<IReadOnlyList<Vehicle>> LoadAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        // Read once, in seed order, tracking nothing: these rows are a
        // catalogue this process will never write back.
        return await db.Vehicles.AsNoTracking().OrderBy(row => row.Seq).Select(row => row.ToVehicle()).ToListAsync();
    }
    // #endregion ef-sources
}

/// <summary>Adapter: the photo manifest, out of the database.</summary>
public sealed class EfPhotoManifestSource(IDbContextFactory<YardDbContext> factory) : IPhotoManifestSource
{
    /// <summary>Every photo entry, in manifest order, read once without change tracking.</summary>
    public async Task<IReadOnlyList<PhotoEntry>> LoadAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Photos.AsNoTracking().OrderBy(row => row.Seq)
            .Select(row => new PhotoEntry(row.File, row.Style, row.Title))
            .ToListAsync();
    }
}

/// <summary>
/// Adapter: bids, which are the only thing here that survives a restart
/// because they are the only thing a visitor creates.
/// </summary>
public sealed class EfBidStore(IDbContextFactory<YardDbContext> factory) : IBidStore
{
    // #region bid-store
    /// <summary>Every stored bid, for BidService to replay into its indexes at startup.</summary>
    public async Task<IReadOnlyList<StoredBid>> LoadAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Bids.AsNoTracking()
            .Select(row => new StoredBid(
                row.UserId,
                row.VehicleId,
                new BidState(row.Amount, row.BidCount, row.WonBuyNow, row.AtMs)))
            .ToListAsync();
    }

    /// <summary>
    /// One row per buyer per vehicle, replaced rather than appended. Called
    /// inside BidService's lock, which is what makes "read the row, decide,
    /// write the row" safe here without the database needing an opinion about
    /// it.
    /// </summary>
    public async Task SaveAsync(string userId, string vehicleId, BidState state)
    {
        // Three tries, then the exception travels. A conflict here means
        // another writer moved this same buyer's row between the read and the
        // write, which takes the one account bidding from two containers at
        // once. The retry reads the row again and writes this bid over it: the
        // rules already ran on this side against the standing this container
        // held, and what the row held in between is not re-examined. Two buyers
        // racing each other are two rows and never meet here: the store keeps no
        // per-vehicle standing, so the compare-and-set standing that would join
        // them is a decision still open (ADR: Three readers with no memory of the project). A fresh
        // context per attempt, because a context that has just thrown a
        // concurrency exception is holding the values that lost.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await WriteAsync(userId, vehicleId, state);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Deliberately empty. The retry is the handling.
            }
        }
    }

    /// <summary>
    /// One attempt at the write: insert the buyer's row for this vehicle, or
    /// update it in place, on a fresh context, and move the concurrency token
    /// where the provider does not move it itself.
    /// </summary>
    private async Task WriteAsync(string userId, string vehicleId, BidState state)
    {
        using var db = await factory.CreateDbContextAsync();
        var existing = await db.Bids.FindAsync(userId, vehicleId);
        if (existing is null)
        {
            existing = new BidRow
            {
                UserId = userId,
                VehicleId = vehicleId,
                Amount = state.Amount,
                BidCount = state.BidCount,
                WonBuyNow = state.WonBuyNow,
                AtMs = state.AtMs,
            };
            db.Bids.Add(existing);
        }
        else
        {
            existing.Amount = state.Amount;
            existing.BidCount = state.BidCount;
            existing.WonBuyNow = state.WonBuyNow;
            existing.AtMs = state.AtMs;
        }

        // SQL Server keeps the token itself; SQLite has no rowversion type, so
        // the store is what moves it. Setting it here rather than in a trigger
        // keeps the difference between the two providers in one readable place
        // (ADR: The SQL Server backend).
        if (db.Database.IsSqlite())
        {
            existing.RowVersion = Guid.NewGuid().ToByteArray();
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Deletes one buyer's bids, every vehicle, in one statement.</summary>
    public async Task ClearAsync(string userId)
    {
        using var db = await factory.CreateDbContextAsync();
        // One person's rows and nobody else's. A delete over the whole table,
        // on a site two strangers can be looking at, would let either of them
        // delete the other's bids (ADR: Reset is one person's start-over).
        await db.Bids.Where(bid => bid.UserId == userId).ExecuteDeleteAsync();
    }
    // #endregion bid-store
}
