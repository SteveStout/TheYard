using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;

namespace TheYard.Api;

// #region machine-sampler
/// <summary>
/// What the container is doing to itself, sampled on a timer and kept in a
/// ring (ADR: What the machines are doing). Every other reading on the Admin
/// tab is taken when something happens: a request, a statement, an operation.
/// Memory is not an event, so nothing on this page could show it until
/// something asked at a fixed interval, which is what this does.
///
/// <para>Fifteen seconds, two hundred and forty of them, which is the last
/// hour and about twenty kilobytes of this container's memory. The ring empties
/// on every roll like every other ring here, and the card says so rather than
/// implying a history the container does not keep.</para>
/// </summary>
public sealed class MachineSampler(int capacity) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    private readonly int _capacity = Math.Max(1, capacity);
    private readonly object _gate = new();
    private readonly Queue<MachineSample> _samples = new();
    private TimeSpan _lastCpu = TimeSpan.Zero;
    private DateTimeOffset _lastAt = DateTimeOffset.MinValue;

    /// <summary>What the runtime says this container may use, which is what a memory number here is a share of.</summary>
    public static double MemoryLimitMb => Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d, 1);

    /// <summary>The processors this process can use, as the runtime sees them inside the container.</summary>
    public static int Processors => Environment.ProcessorCount;

    public IReadOnlyList<MachineSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }

    /// <summary>One reading. Public so the suite can take one without waiting a quarter of a minute for the timer.</summary>
    public MachineSample Sample()
    {
        using var process = Process.GetCurrentProcess();
        var at = DateTimeOffset.UtcNow;
        var cpu = process.TotalProcessorTime;
        var info = GC.GetGCMemoryInfo();

        // The share of the container's processors this process spent since the
        // last reading. The first reading has nothing to subtract from and
        // reports no percentage rather than one averaged over the whole life
        // of the process, which is a different number wearing the same label.
        double? percent = null;
        if (_lastAt != DateTimeOffset.MinValue)
        {
            double elapsed = (at - _lastAt).TotalMilliseconds;
            if (elapsed > 0)
            {
                percent = Math.Round((cpu - _lastCpu).TotalMilliseconds / (elapsed * Processors) * 100, 1);
            }
        }
        _lastCpu = cpu;
        _lastAt = at;

        var sample = new MachineSample(
            at,
            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1),
            Math.Round(info.HeapSizeBytes / 1024d / 1024d, 1),
            percent,
            process.Threads.Count,
            GC.CollectionCount(0),
            GC.CollectionCount(2));

        lock (_gate)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > _capacity)
            {
                _samples.Dequeue();
            }
        }
        return sample;
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(Every);
        Sample();
        while (await timer.WaitForNextTickAsync(stopping))
        {
            Sample();
        }
    }
}

/// <summary>One reading of the container: memory as the operating system and the runtime each see it, the processor share since the last reading, and what the collector has done.</summary>
public sealed record MachineSample(
    DateTimeOffset At,
    double WorkingSetMb,
    double ManagedMb,
    double HeapMb,
    double? CpuPercent,
    int Threads,
    int Gen0Collections,
    int Gen2Collections);
// #endregion machine-sampler

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
    public const string Query = """
        SELECT TOP ({rows})
            end_time AS At,
            avg_cpu_percent AS CpuPercent,
            avg_data_io_percent AS DataIoPercent,
            avg_log_write_percent AS LogWritePercent,
            avg_memory_usage_percent AS MemoryPercent,
            max_worker_percent AS WorkerPercent
        FROM sys.dm_db_resource_stats
        ORDER BY end_time DESC
        """;

    public static async Task<StoreLoad> ReadAsync(Backend? relational, int rows, CancellationToken cancellation, ILogger? logger = null)
    {
        if (relational?.Contexts is null)
        {
            return StoreLoad.Absent("this container has no relational store, or it did not come up");
        }

        await using var db = await relational.Contexts.CreateDbContextAsync(cancellation);
        if (!db.Database.IsSqlServer())
        {
            return StoreLoad.Absent($"the relational store here is {relational.Name}, which keeps no resource view");
        }

        try
        {
            // The only value put into this statement is the row count, which
            // is an integer this method was called with and clamped above, and
            // never anything a request supplies. The view takes no parameters
            // and returns whatever the last hour holds.
            string sql = Query.Replace("{rows}", Math.Clamp(rows, 1, 240).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var rowsRead = await db.Database.SqlQueryRaw<ResourceStatRow>(sql).ToListAsync(cancellation);
            return new StoreLoad(true, null, rowsRead);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or OperationCanceledException)
        {
            // The type on the card, never the message: this reading is served
            // on a public page and a database message carries a server name.
            // The message goes to the container's log, where an operator can
            // read it and where the first live read of this needed it: the
            // view answered with an exception on both sites and the card could
            // only say that much.
            logger?.LogWarning(ex, "The relational store's resource view did not answer");
            return StoreLoad.Absent($"the resource view did not answer ({ex.GetType().Name}); the container's log carries the reason");
        }
    }
}

/// <summary>One fifteen-second interval as the database reports it, every figure a percentage of what the tier allows.</summary>
public sealed record ResourceStatRow(
    DateTime At,
    double CpuPercent,
    double DataIoPercent,
    double LogWritePercent,
    double MemoryPercent,
    double WorkerPercent);

/// <summary>The relational store's own reading, or the reason there is not one.</summary>
public sealed record StoreLoad(bool Available, string? Note, IReadOnlyList<ResourceStatRow> Rows)
{
    public static StoreLoad Absent(string note) => new(false, note, []);
}
// #endregion resource-stats

// #region document-load
/// <summary>
/// The document store has no memory reading to give: Azure Cosmos DB is sold
/// by request unit and says nothing about the machine underneath, which is the
/// honest difference between the two stores on this card. What it does say is
/// what each operation cost, and this folds the operations ring into request
/// units a minute against the thousand a second the free tier allows.
/// </summary>
public static class DocumentLoad
{
    /// <summary>The free tier's allowance, which every number here is a share of.</summary>
    public const int FreeRequestUnitsPerSecond = 1000;

    public static DocumentLoadView From(IReadOnlyList<StoreOperation> operations, string store)
    {
        if (operations.Count == 0)
        {
            return new DocumentLoadView(store, false, "nothing has been sent to the document store in this container yet", 0, 0, null, null, []);
        }

        var minutes = operations
            .GroupBy(operation => new DateTimeOffset(operation.At.Year, operation.At.Month, operation.At.Day, operation.At.Hour, operation.At.Minute, 0, TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group => new DocumentMinute(
                group.Key,
                Math.Round(group.Sum(operation => operation.RequestCharge), 2),
                group.Count(),
                // The share of a second's free allowance this minute's charge
                // would be if it had all arrived in one second, which is the
                // number that matters: the allowance is per second, and a
                // minute of steady reads is nowhere near it.
                Math.Round(group.Sum(operation => operation.RequestCharge) / 60 / FreeRequestUnitsPerSecond * 100, 3)))
            .ToList();

        long[] durations = operations.Select(operation => operation.DurationMs).ToArray();
        return new DocumentLoadView(
            store,
            true,
            null,
            Math.Round(operations.Sum(operation => operation.RequestCharge), 2),
            operations.Count,
            Percentiles.Of(durations, 50),
            Percentiles.Of(durations, 95),
            minutes);
    }
}

/// <summary>One minute of the operations ring: what it cost, how many operations, and what share of a second of the free allowance that would be.</summary>
public sealed record DocumentMinute(DateTimeOffset At, double RequestUnits, int Operations, double ShareOfFreePercent);

/// <summary>The document store's side of the card.</summary>
public sealed record DocumentLoadView(
    string Store,
    bool Available,
    string? Note,
    double RequestUnits,
    int Operations,
    long? P50Ms,
    long? P95Ms,
    IReadOnlyList<DocumentMinute> Minutes);
// #endregion document-load
