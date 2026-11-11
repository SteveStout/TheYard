using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.History;

namespace TheYard.Infrastructure.Shared.History;

/// <summary>
/// The last read of the bill kept in the relational store, one row, so a roll does not lose it (ADR: What Azure charges,
/// the addendum on keeping the read).
///
/// <para>The table is published by a person, like every other table on SQL Server (ADR: Data first, and the database in
/// source control), so this store asks whether it is there and keeps nothing until it is; on SQLite the migration makes
/// it. The contexts arrive when the relational store is attached, at startup or later, so they are asked for at each
/// call and a store that is not attached yet is simply not there.</para>
/// </summary>
/// <param name="contexts">The relational store's quiet context factory, or null while the store is not attached.</param>
public sealed class EfCostStore(Func<IDbContextFactory<YardDbContext>?> contexts)
{
    private bool _present;

    /// <summary>Whether the kept read's table can be read and written now, and if not, why, in words the card can show.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<CostHistoryAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (contexts() is not { } factory)
        {
            return new CostHistoryAvailability(false, "the relational store is not attached");
        }
        if (_present)
        {
            return new CostHistoryAvailability(true, "kept in the relational store");
        }
        try
        {
            using var db = await factory.CreateDbContextAsync(cancellation);
            if (!db.Database.IsSqlite())
            {
                var tables = await db.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.tables").ToListAsync(cancellation);
                if (!tables.Contains("CostReads", StringComparer.OrdinalIgnoreCase))
                {
                    return new CostHistoryAvailability(false, "the published schema is missing CostReads; publish api/TheYard.Database");
                }
            }
            await db.Set<CostReadRow>().AsNoTracking().Take(1).ToListAsync(cancellation);
            _present = true;
            return new CostHistoryAvailability(true, "kept in the relational store");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type and not the message: a driver's message names the server.
            return new CostHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    /// <summary>The kept read, or null when none has been kept.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<CostKept?> LoadAsync(CancellationToken cancellation)
    {
        using var db = await contexts()!.CreateDbContextAsync(cancellation);
        var row = await db.Set<CostReadRow>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == CostReadRow.TheRow, cancellation);
        return row is null
            ? null
            : new CostKept(
                JsonSerializer.Deserialize<CostDay[]>(row.Days) ?? [],
                JsonSerializer.Deserialize<CostForecastDay[]>(row.Forecast) ?? [],
                row.ReadAt);
    }

    /// <summary>Keeps a read, replacing the last.</summary>
    /// <param name="kept">The read and when it was taken.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task SaveAsync(CostKept kept, CancellationToken cancellation)
    {
        using var db = await contexts()!.CreateDbContextAsync(cancellation);
        var row = await db.Set<CostReadRow>().SingleOrDefaultAsync(r => r.Id == CostReadRow.TheRow, cancellation);
        string days = JsonSerializer.Serialize(kept.Days);
        string forecast = JsonSerializer.Serialize(kept.Forecast);
        DateTimeOffset at = kept.ReadAt ?? throw new ArgumentException("a kept read names when it was read", nameof(kept));
        if (row is null)
        {
            db.Set<CostReadRow>().Add(new CostReadRow { Id = CostReadRow.TheRow, ReadAt = at, Days = days, Forecast = forecast });
        }
        else
        {
            (row.ReadAt, row.Days, row.Forecast) = (at, days, forecast);
        }
        await db.SaveChangesAsync(cancellation);
    }
}
