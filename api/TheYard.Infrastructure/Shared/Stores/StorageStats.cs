// How full the relational store is, for the Azure card on the Admin tab: the data files' size
// beside the most the tier allows. It sits with the relational adapters because it is a raw
// query against the database; the host's hourly reading (TheYard.Api/HourlyReadings.cs) asks it.
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure.Shared.Stores;

// #region storage-stats
/// <summary>
/// Azure SQL Database's own size: the data files from <c>sys.database_files</c>
/// in megabytes, and the tier's ceiling from <c>DATABASEPROPERTYEX</c>. Both are
/// readable by any user of the database, so the reading needs no role the
/// container's identity does not already hold. The tier refuses writes when
/// the data files reach the ceiling, which is the line this reading is for.
/// On SQLite, or a store that did not come up, there is no reading.
/// </summary>
public static class StorageStats
{
    /// <summary>Data files only (type 0); the log is the platform's to size.</summary>
    public const string Query = """
        SELECT
            CAST(SUM(CAST(size AS bigint)) * 8 / 1024.0 AS float) AS UsedMb,
            CAST(CAST(DATABASEPROPERTYEX(DB_NAME(), 'MaxSizeInBytes') AS bigint) / 1048576.0 AS float) AS MaxMb
        FROM sys.database_files
        WHERE type = 0
        """;

    /// <summary>The store's size and its ceiling, or null where there is no Azure SQL Database to ask or it did not answer.</summary>
    public static async Task<StorageReading?> ReadAsync(IDbContextFactory<YardDbContext>? contexts, CancellationToken cancellation)
    {
        if (contexts is null)
        {
            return null;
        }
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellation);
            if (!db.Database.IsSqlServer())
            {
                return null;
            }
            var rows = await db.Database.SqlQueryRaw<StorageReading>(Query).ToListAsync(cancellation);
            return rows.FirstOrDefault();
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or OperationCanceledException)
        {
            // A reading is a nicety on a public card; a store that will not say is no reading, never an error.
            return null;
        }
    }
}

/// <summary>The relational store's data files and the most its tier allows, in megabytes.</summary>
/// <param name="UsedMb">The data files' allocated size.</param>
/// <param name="MaxMb">The tier's ceiling for the data files.</param>
public sealed record StorageReading(double UsedMb, double MaxMb);
// #endregion storage-stats
