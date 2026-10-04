// The machines kept past the hour: the recorder that folds each finished minute and writes it to
// the history port, and the reader that serves the kept day, week and month back to the Admin tab.
// They are their own file because they are the only part of the Machines card that outlives a
// roll of the container. The rest of the card starts in Machines.cs.
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Api;

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
        var start = minute.UtcMinute;
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
        var traffic = TrafficMinutes.From((requests ?? []).Where(request => request.At >= start && request.At < end).ToList()).SingleOrDefault();

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
            traffic?.Requests ?? 0,
            traffic?.P50Ms,
            traffic?.P95Ms,
            traffic?.ServerErrors ?? 0,
            traffic?.ClientErrors ?? 0);
    }

    /// <summary>
    /// The loop: wait for a minute to end and settle, fold it, and hand it to
    /// the history port; with no store to keep minutes in, wait ten minutes
    /// and ask again. A failure is logged by type and the loop goes on.
    /// </summary>
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
                await Task.Delay(now.UtcMinute.AddMinutes(1).Add(Settle) - now, stopping);

                var minute = DateTimeOffset.UtcNow.UtcMinute.AddMinutes(-1);
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
    /// <summary>The lock that guards the cache, since requests read and fill it at the same time.</summary>
    private readonly object _gate = new();

    /// <summary>The last answer for each window, by the window's name, with when it was read.</summary>
    private readonly Dictionary<string, (DateTimeOffset At, object View)> _cached = new(StringComparer.Ordinal);

    /// <summary>
    /// One window as the card reads it: an unknown or hourly window answers
    /// empty (the hour comes from the sampler's ring, not from here), a store
    /// that keeps nothing answers with its reason, and anything else is the
    /// window's buckets and totals, served from the cache when under a minute old.
    /// </summary>
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
            // The window's counts, added up from the buckets' own counts. The
            // charts draw rates; the numbers over them are these.
            totals = MachineFolding.Totals(buckets),
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
