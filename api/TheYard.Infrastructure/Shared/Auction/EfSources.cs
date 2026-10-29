// The three ports, answered from the database: the seed catalogue, the photo manifest, and the
// bids. These are the adapters the composition root wires when the relational store opens.
// The rest of the relational plumbing has a file of its own beside this one:
//   VehicleRows.cs     row to domain and back
//   DatabaseState.cs   whether the store is usable, and one sentence about why
//   EfBidStore.Standing.cs   each vehicle's standing, moved with a compare-and-set
//   YardDatabase.cs    bringing the database up once at startup
//   SeedResult.cs      what the first boot found
//   YardSeed.cs        filling an empty catalogue from the JSON files
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;
using TheYard.Data;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>
/// Adapter: the seed catalogue, out of the database. Same port the JSON file
/// reader implements, and the synthetic scale-up still wraps it, so nothing
/// above this layer depends on which storage is underneath.
/// </summary>
public sealed class EfVehicleSource(IDbContextFactory<YardDbContext> factory) : IVehicleSource
{
    // #region ef-sources
    /// <summary>Every seed vehicle, in the order it was seeded, read once without change tracking.</summary>
    /// <param name="cancellation">The caller's; in a running container this is the host's stopping token, because the warm-up is what asks.</param>
    public async Task<IReadOnlyList<Vehicle>> LoadAsync(CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync(cancellation);
        // Read once, in seed order, tracking nothing: these rows are a
        // catalogue this process will never write back.
        return await db.Vehicles.AsNoTracking().OrderBy(row => row.Seq).Select(row => row.ToVehicle()).ToListAsync(cancellation);
    }
    // #endregion ef-sources
}

/// <summary>Adapter: the photo manifest, out of the database.</summary>
public sealed class EfPhotoManifestSource(IDbContextFactory<YardDbContext> factory) : IPhotoManifestSource
{
    /// <summary>Every photo entry, in manifest order, read once without change tracking.</summary>
    /// <param name="cancellation">The caller's, carried to EF Core.</param>
    public async Task<IReadOnlyList<PhotoEntry>> LoadAsync(CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync(cancellation);
        return await db.Photos.AsNoTracking().OrderBy(row => row.Seq)
            .Select(row => new PhotoEntry(row.File, row.Style, row.Title))
            .ToListAsync(cancellation);
    }
}

/// <summary>
/// Adapter: bids and the standing they add up to, which are the only things
/// here that change after startup, because they are the only things a visitor
/// creates. The standing half is in EfBidStore.Standing.cs.
/// </summary>
public sealed partial class EfBidStore(IDbContextFactory<YardDbContext> factory) : IBidStore
{
    // #region bid-store
    /// <summary>Every stored bid, for BidBook to replay into its indexes at startup.</summary>
    /// <param name="cancellation">The caller's; the replay's starter lends it, and in a container that is the host.</param>
    public async Task<IReadOnlyList<StoredBid>> LoadAsync(CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync(cancellation);
        return await db.Bids.AsNoTracking().Select(row => new StoredBid(
                row.UserId,
                row.VehicleId,
                new BidState(row.Amount, row.BidCount, row.WonBuyNow, row.AtMs)))
            .ToListAsync(cancellation);
    }

    /// <summary>
    /// Every buyer's row on one vehicle. There is no index on VehicleId alone,
    /// so this reads the table; it runs only when a vehicle has no standing yet,
    /// once per vehicle, on a table of visitor bids.
    /// </summary>
    /// <param name="vehicleId">The vehicle.</param>
    /// <param name="cancellation">The caller's. A table read is the most expensive thing this adapter does, so it is the one most worth stopping.</param>
    public async Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId, CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync(cancellation);
        return await db.Bids.AsNoTracking().Where(row => row.VehicleId == vehicleId).Select(row => new StoredBid(
                row.UserId,
                row.VehicleId,
                new BidState(row.Amount, row.BidCount, row.WonBuyNow, row.AtMs)))
            .ToListAsync(cancellation);
    }

    /// <summary>
    /// The buyer's row and the vehicle's standing in one transaction, the
    /// standing first with a compare-and-set on its version. An update that
    /// names a version no longer there changes no row, and an insert where
    /// another writer has just inserted breaks the key; either way the
    /// transaction is rolled back and the answer is false, so BidBook reads
    /// again and runs the rules again. The buyer's row keeps its own token,
    /// which catches one account writing from two sites at once, and that too
    /// answers false rather than writing over what it never saw.
    ///
    /// <para>The token this takes is never handed to the transaction, and that is the whole of
    /// what the ports record promised: a write abandoned between the standing and the buyer's
    /// row would leave the store holding a standing nothing has a row for. The caller above
    /// has already stopped honouring cancellation by the time it gets here, so the parameter
    /// exists to keep one shape across the port and not to be used (ADR: The ports learn to
    /// wait, the addendum on the token).</para>
    /// </summary>
    public async Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion, CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (expectedVersion is not null && !await TryMoveStandingAsync(db, vehicleId, standing, long.Parse(expectedVersion, CultureInfo.InvariantCulture), CancellationToken.None))
        {
            return false;
        }

        if (expectedVersion is null)
        {
            db.Standings.Add(RowOf(vehicleId, standing, version: 1));
        }

        await StageBuyerRowAsync(db, userId, vehicleId, state, cancellation);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException) when (expectedVersion is null)
        {
            // An insert that broke a key lost to another writer's insert of the
            // same standing. Anything else that broke is a real failure and
            // travels.
            await transaction.RollbackAsync();
            if (await StandingExistsAsync(vehicleId, CancellationToken.None))
            {
                return false;
            }
            throw;
        }

        await transaction.CommitAsync();
        return true;
    }

    /// <summary>
    /// The buyer's row for this vehicle, added or changed in place on the
    /// context, with the concurrency token moved where the provider does not
    /// move it itself. Nothing is written until the caller saves.
    /// </summary>
    private static async Task StageBuyerRowAsync(YardDbContext db, string userId, string vehicleId, BidState state, CancellationToken cancellation)
    {
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
    }

    /// <summary>
    /// Deletes one buyer's rows, every vehicle, and the standing of each of
    /// those vehicles, in one transaction. The standings go so the next bid on
    /// each vehicle works its standing out again from whoever is left.
    ///
    /// <para>Like the bid write, the token is not given to the transaction: the standings and
    /// the rows go together or not at all, and a reset stopped between them would leave
    /// vehicles with no standing and rows still claiming one.</para>
    /// </summary>
    public async Task ClearAsync(string userId, CancellationToken cancellation)
    {
        using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // One person's rows and nobody else's. A delete over the whole table,
        // on a site two strangers can be looking at, would let either of them
        // delete the other's bids (ADR: Reset is one person's start-over).
        var touched = await db.Bids.Where(bid => bid.UserId == userId).Select(bid => bid.VehicleId).ToListAsync();
        await db.Standings.Where(row => touched.Contains(row.VehicleId)).ExecuteDeleteAsync();
        await db.Bids.Where(bid => bid.UserId == userId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }
    // #endregion bid-store
}
