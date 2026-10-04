// The relational store's own reading of itself, for the Machines card on the Admin tab: the
// statement, the read, and the sentence a public card shows when the read does not happen. It
// sits with the relational adapters because it is a raw query against the database; the host's
// Machines card (TheYard.Api/Machines.cs) only asks it for a reading.
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace TheYard.Infrastructure;

// #region resource-stats
/// <summary>
/// Azure SQL Database's own reading of itself: `sys.dm_db_resource_stats`,
/// which every tier keeps for the last hour at fifteen-second intervals and
/// which costs nothing to read. On the Basic tier the percentages are of five
/// DTUs, so a number here is a share of a very small machine rather than of a
/// large one, which is the whole point of showing them beside a bill of $4.90.
///
/// <para>The view exists on Azure SQL Database and nowhere else: on SQLite,
/// on SQL Server in a container, and on a database that has not come up, the
/// reading is absent and the card says which of those it is rather than
/// drawing a zero.</para>
/// </summary>
public static class ResourceStats
{
    /// <summary>
    /// Every percentage is cast to float on the way out. The view returns them
    /// as `decimal(5,2)`, and a decimal read into a double throws an
    /// InvalidCastException, which would turn every read of this endpoint into
    /// an error the moment the identity is allowed to run the query at all.
    /// The cast is in the statement rather than in the type because the
    /// reading is a percentage a chart draws, not money.
    /// </summary>
    public const string Query = """
        SELECT TOP ({rows})
            end_time AS At,
            CAST(avg_cpu_percent AS float) AS CpuPercent,
            CAST(avg_data_io_percent AS float) AS DataIoPercent,
            CAST(avg_log_write_percent AS float) AS LogWritePercent,
            CAST(avg_memory_usage_percent AS float) AS MemoryPercent,
            CAST(max_worker_percent AS float) AS WorkerPercent
        FROM sys.dm_db_resource_stats
        ORDER BY end_time DESC
        """;

    /// <summary>
    /// The read over a store's context factory, or an absent reading when there is no
    /// relational store. The recorder that keeps a minute at a time hands in the quiet one, with no interceptor and no
    /// command logging, for the reason the activity counters use it: a read a
    /// minute, outside any request, would otherwise be the newest statement on
    /// the SQL card for ever (ADR: What the machines are doing).
    /// </summary>
    public static async Task<StoreLoad> ReadAsync(IDbContextFactory<YardDbContext>? contexts, string storeName, int rows, CancellationToken cancellation, ILogger? logger = null)
    {
        if (contexts is null)
        {
            return StoreLoad.Absent("this container has no relational store, or it did not come up");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellation);
        if (!db.Database.IsSqlServer())
        {
            return StoreLoad.Absent($"the relational store here is {storeName}, which keeps no resource view");
        }

        try
        {
            // The only value put into this statement is the row count, which
            // is an integer this method was called with and clamped above, and
            // never anything a request supplies. The view takes no parameters
            // and returns whatever the last hour holds.
            string sql = Query.Replace("{rows}", Math.Clamp(rows, 1, 240).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var rowsRead = await db.Database.SqlQueryRaw<ResourceStatRow>(sql).ToListAsync(cancellation);
            return new StoreLoad(true, null, rowsRead.Select(InUtc).ToList());
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or OperationCanceledException)
        {
            // The database's own error number, and a sentence for the ones
            // worth a sentence. Never the message: this reading is served on a
            // public page and a message carries a server name. The number does
            // not, and it is the difference between "it did not answer" and
            // "it answered, and said this identity may not read it", which is
            // the difference someone reading the card needs to act on.
            logger?.LogWarning(ex, "The relational store's resource view did not answer");
            return StoreLoad.Absent(ReasonFor((ex as SqlException)?.Number, ex.GetType().Name));
        }
    }

    /// <summary>
    /// The view's end_time is UTC and arrives with no kind on it, so without
    /// this the wire would write it with no offset and a browser would read it
    /// as its own local time, drawing the database hours away from the
    /// container beside it. Marked UTC here, it goes out with its Z like every
    /// other instant on the card.
    /// </summary>
    public static ResourceStatRow InUtc(ResourceStatRow row) =>
        row with { At = DateTime.SpecifyKind(row.At, DateTimeKind.Utc) };

    /// <summary>What to say on a public card about a reading that did not happen, from the database's own error number.</summary>
    public static string ReasonFor(int? number, string typeName)
    {
        return number switch
        {
            // The three shapes of "permission denied" this view answers with.
            // 262 is the one a live site answers with: the generic
            // "<permission> permission denied in database" that carries the
            // permission's name in a message this card does not print. Reading
            // the view needs VIEW DATABASE STATE, which db_datareader and
            // db_datawriter do not carry, and those are the two roles this
            // container's identity holds (ADR: The SQL Server backend). One
            // GRANT is the whole difference.
            229 or 262 or 300 => "the store keeps this reading, and this container's identity may not read it: the view needs VIEW DATABASE STATE, which the two roles the identity holds do not carry",
            null => $"the resource view did not answer ({typeName})",
            _ => $"the resource view answered with database error {number}",
        };
    }
}

/// <summary>One fifteen-second interval as the database reports it, every figure a percentage of what the tier allows.</summary>
/// <param name="At">The end of the fifteen-second interval, UTC.</param>
/// <param name="CpuPercent">Processor use, as a percentage of the tier's limit.</param>
/// <param name="DataIoPercent">Data file reads and writes, as a percentage of the tier's limit.</param>
/// <param name="LogWritePercent">Transaction log writes, as a percentage of the tier's limit.</param>
/// <param name="MemoryPercent">Memory use, as a percentage of the tier's limit.</param>
/// <param name="WorkerPercent">Concurrent workers, as a percentage of the tier's limit.</param>
public sealed record ResourceStatRow(
    DateTime At,
    double CpuPercent,
    double DataIoPercent,
    double LogWritePercent,
    double MemoryPercent,
    double WorkerPercent);

/// <summary>The relational store's own reading, or the reason there is not one.</summary>
/// <param name="Available">True when the database answered with its reading.</param>
/// <param name="Note">Why there is no reading, or null when there is one.</param>
/// <param name="Rows">The intervals the database reported, empty when there is no reading.</param>
public sealed record StoreLoad(bool Available, string? Note, IReadOnlyList<ResourceStatRow> Rows)
{
    /// <summary>A reading that did not happen, with the sentence the card shows in its place.</summary>
    public static StoreLoad Absent(string note) => new(false, note, []);
}
// #endregion resource-stats
