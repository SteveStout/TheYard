using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;

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
    /// <summary>
    /// Every percentage is cast to float on the way out. The view returns them
    /// as `decimal(5,2)`, and a decimal read into a double is the
    /// InvalidCastException both live sites answered this endpoint with for
    /// four minutes on 2026-09-19, the minute the grant that let the query run
    /// at all went in. The cast is in the statement rather than in the type
    /// because the reading is a percentage a chart draws, not money.
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

    public static Task<StoreLoad> ReadAsync(Backend? relational, int rows, CancellationToken cancellation, ILogger? logger = null) =>
        ReadAsync(relational?.Contexts, relational?.Name ?? "absent", rows, cancellation, logger);

    /// <summary>
    /// The same read over any context factory. The recorder that keeps a
    /// minute at a time hands in the quiet one, with no interceptor and no
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
            return new StoreLoad(true, null, rowsRead);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or OperationCanceledException)
        {
            // The database's own error number, and a sentence for the ones
            // worth a sentence. Never the message: this reading is served on a
            // public page and a message carries a server name. The number does
            // not, and it is the difference between "it did not answer" and
            // "it answered, and said this identity may not read it", which is
            // what the first live read of this card needed and did not have.
            logger?.LogWarning(ex, "The relational store's resource view did not answer");
            return StoreLoad.Absent(ReasonFor((ex as SqlException)?.Number, ex.GetType().Name));
        }
    }

    /// <summary>What to say on a public card about a reading that did not happen, from the database's own error number.</summary>
    public static string ReasonFor(int? number, string typeName)
    {
        return number switch
        {
            // The three shapes of "permission denied" this view answers with,
            // and 262 is the one the live site actually answered on
            // 2026-09-19: the generic "<permission> permission denied in
            // database" that carries the permission's name in a message this
            // card does not print. Reading the view needs VIEW DATABASE STATE,
            // which db_datareader and db_datawriter do not carry, and those are
            // the two roles this container's identity holds (ADR: The SQL
            // Server backend). One GRANT is the whole difference.
            229 or 262 or 300 => "the store keeps this reading, and this container's identity may not read it: the view needs VIEW DATABASE STATE, which the two roles the identity holds do not carry",
            null => $"the resource view did not answer ({typeName})",
            _ => $"the resource view answered with database error {number}",
        };
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

// #region machine-recorder
/// <summary>
/// Keeps a minute at a time (ADR: What the machines are doing). The sampler's
/// ring is an hour of this process's memory and a roll empties it, so a day,
/// a week and a month could not be drawn from it.
/// Twenty seconds after each minute ends, this folds the minute that just
/// finished, out of what the process already measures, and hands it to the
/// port: the four samples, the relational store's own rows for that minute,
/// and what the document store charged in it.
///
/// <para>Nothing new is measured for it except one read of the resource view a
/// minute, on the quiet context. With no store to keep minutes in it does
/// nothing at all, and asks again every ten minutes whether there is one.</para>
/// </summary>
public sealed class MachineRecorder(
    MachineSampler sampler,
    IMachineHistory history,
    string site,
    Func<CancellationToken, Task<StoreLoad>> relational,
    Func<IReadOnlyList<StoreOperation>> operations,
    Func<IReadOnlyList<RequestEntry>> requests,
    ILogger<MachineRecorder> logger) : BackgroundService
{
    /// <summary>How long after a minute ends it is folded: long enough for its last sample and the store's last row to exist.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    /// <summary>
    /// One minute out of the rings, or null when the sampler took no sample in
    /// it, which is a process that was not running: there is nothing to keep,
    /// and a gap on the chart is the true picture of a gap.
    /// </summary>
    public static MachineMinute? Fold(
        DateTimeOffset minute,
        string site,
        double memoryLimitMb,
        IReadOnlyList<MachineSample> samples,
        IReadOnlyList<ResourceStatRow> rows,
        IReadOnlyList<StoreOperation> operations,
        IReadOnlyList<RequestEntry>? requests = null)
    {
        var start = MachineFolding.MinuteOf(minute);
        var end = start.AddMinutes(1);
        var taken = samples.Where(sample => sample.At >= start && sample.At < end).ToList();
        if (taken.Count == 0)
        {
            return null;
        }

        // The view's end_time is UTC with no kind on it.
        var read = rows
            .Where(row =>
            {
                var at = new DateTimeOffset(DateTime.SpecifyKind(row.At, DateTimeKind.Utc));
                return at >= start && at < end;
            })
            .ToList();
        var charged = operations.Where(operation => operation.At >= start && operation.At < end).ToList();
        // The request ring already leaves out the Admin tab watching itself
        // and the page sweep, so this is a visitor's minute and not this page's.
        var answered = (requests ?? []).Where(request => request.At >= start && request.At < end).ToList();
        long[] durations = answered.Select(request => request.DurationMs).ToArray();

        return new MachineMinute(
            start,
            site,
            memoryLimitMb,
            Math.Round(taken.Average(sample => sample.WorkingSetMb), 1),
            Math.Round(taken.Max(sample => sample.WorkingSetMb), 1),
            Math.Round(taken.Average(sample => sample.ManagedMb), 1),
            MachineFolding.MeanOf(taken.Select(sample => sample.CpuPercent)),
            MachineFolding.MaxOf(taken.Select(sample => sample.CpuPercent)),
            MachineFolding.MeanOf(read.Select(row => (double?)row.CpuPercent)),
            MachineFolding.MeanOf(read.Select(row => (double?)row.MemoryPercent)),
            MachineFolding.MeanOf(read.Select(row => (double?)row.DataIoPercent)),
            Math.Round(charged.Sum(operation => operation.RequestCharge), 2),
            charged.Count,
            answered.Count,
            durations.Length == 0 ? null : (double?)Percentiles.Of(durations, 50),
            durations.Length == 0 ? null : (double?)Percentiles.Of(durations, 95),
            answered.Count(request => request.Status >= 500),
            answered.Count(request => request.Status is >= 400 and < 500));
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (!(await history.AvailabilityAsync(stopping)).Available)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stopping);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                await Task.Delay(MachineFolding.MinuteOf(now).AddMinutes(1).Add(Settle) - now, stopping);

                var minute = MachineFolding.MinuteOf(DateTimeOffset.UtcNow).AddMinutes(-1);
                var load = await relational(stopping);
                var folded = Fold(minute, site, MachineSampler.MemoryLimitMb, sampler.Snapshot(), load.Rows, operations(), requests());
                if (folded is not null)
                {
                    await history.AppendAsync(folded, stopping);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The type, never the message, and the loop goes on: a minute
                // that could not be kept is a gap on a chart, not an outage.
                logger.LogWarning("A minute of the machines could not be kept ({Exception})", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMinutes(1), stopping).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}

/// <summary>
/// The kept windows, read back for the card, each cached for a minute: the
/// Admin tab is a page somebody leaves open, a month is a grouped query over
/// forty thousand documents, and the answer cannot change more often than a
/// minute is written.
/// </summary>
public sealed class MachineHistoryReader(IMachineHistory history, string site)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, object View)> _cached = new(StringComparer.Ordinal);

    public async Task<object> ReadAsync(string? window, DateTimeOffset now, CancellationToken cancellation)
    {
        var chosen = MachineWindows.Parse(window);
        if (chosen is null)
        {
            return new { window = MachineWindows.Hour, kept = false, available = true, note = (string?)null, site, bucket_minutes = 0, as_of = now, buckets = Array.Empty<MachineBucket>() };
        }

        var (length, grain, name) = chosen.Value;
        lock (_gate)
        {
            if (_cached.TryGetValue(name, out var hit) && now - hit.At < TimeSpan.FromMinutes(1))
            {
                return hit.View;
            }
        }

        var availability = await history.AvailabilityAsync(cancellation);
        int bucketMinutes = grain switch
        {
            MachineGrain.FiveMinutes => 5,
            MachineGrain.Hour => 60,
            _ => 240,
        };
        if (!availability.Available)
        {
            return new { window = name, kept = true, available = false, note = (string?)availability.Reason, site, bucket_minutes = bucketMinutes, as_of = now, buckets = Array.Empty<MachineBucket>() };
        }

        var buckets = await history.QueryAsync(site, now - length, grain, cancellation);
        var view = new
        {
            window = name,
            kept = true,
            available = true,
            note = (string?)(buckets.Count == 0 ? "nothing has been kept for this window yet; a minute is written a minute after it ends" : availability.Reason),
            site,
            bucket_minutes = bucketMinutes,
            // The instant the window ends at, so the page lays its timeline out
            // from the server's clock and not the browser's.
            as_of = now,
            buckets,
        };
        lock (_gate)
        {
            _cached[name] = (now, view);
        }

        return view;
    }
}
// #endregion machine-recorder

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
