// The machines card and the memory-against-traffic card as their two queries read them (ADR-115's fourth rung).
// The bodies are the two routes' bodies as they were, moved; the windows they read are named there.

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The machines: what the container and the stores are doing, and memory against traffic.</summary>
public sealed class MachineCardReadings(MachineSampler sampler, MachineHistoryReader kept, Backends backends, StoreRingBuffer storeLog, RequestRingBuffer requestLog, HostStart start) : IMachineReadings
{
    /// <inheritdoc/>
    public async Task<object> MachinesAsync(string? window, CancellationToken cancellation)
    {
        var startedAt = start.At;
        var relational = backends.Named(StoreKeys.Relational);
        var load = relational is null
            ? StoreLoad.Absent("this container has no relational store, or it did not come up")
            : await relational.ReadLoadAsync(RingSizes.MachineSamples, cancellation);
        var now = Clocks.UtcNow();
        var document = DocumentLoad.From(storeLog.Snapshot(), backends.Named(StoreKeys.Document)?.Name ?? "Azure Cosmos DB", now);
        return new
        {
            // The hour below is this process's own memory and is always here. A
            // wider window is read from the store, in buckets sized to it.
            windows = MachineWindows.Names,
            history = await kept.ReadAsync(window, now, cancellation),
            // The request ring a minute at a time, in the unit a kept minute is
            // written in, so the hour and the month are one chart. The ring is
            // a number of requests and not an hour: when it is full its oldest
            // minute is marked, and the page says how many minutes it reaches.
            traffic = new { ring = RingSizes.RequestRing, minutes = TrafficMinutes.OfRing(requestLog.Snapshot(), RingSizes.RequestRing) },
            container = new
            {
                memory_limit_mb = MachineSampler.MemoryLimitMb,
                processors = MachineSampler.Processors,
                uptime_seconds = (long)(now - startedAt).TotalSeconds,
                every_seconds = (int)MachineSampler.Every.TotalSeconds,
                samples = sampler.Snapshot(),
                // The collector's own reading, once, now: what ADR: The store does the work
                // measured before and after, and what MEMORY.md teaches to read.
                gc = MachineSampler.GcReading(),
                // Which catalogues this process is holding right now. Until the store did the
                // work, a hundred thousand vehicles per store was most of the memory above; the
                // count held in memory reads 0 once the store answers (ADR: The store does the work).
                catalogues = backends.All.Select(backend => new
                {
                    store = backend.Name,
                    serves = ReferenceEquals(backend, backends.Default),
                    loaded = backend.CatalogueLoaded,
                    held = backend.CatalogueHeld(),
                }),
            },
            relational = new
            {
                store = relational?.Name ?? "no relational store on this container",
                available = load.Available,
                note = load.Note,
                rows = load.Rows,
            },
            document = new
            {
                store = document.Store,
                available = document.Available,
                note = document.Note,
                request_units = document.RequestUnits,
                operations = document.Operations,
                p50_ms = document.P50Ms,
                p95_ms = document.P95Ms,
                free_request_units_per_second = DocumentLoad.FreeRequestUnitsPerSecond,
                // How many minutes back the operations ring reaches, so the
                // request units above are a total over a stated stretch.
                span_minutes = document.SpanMinutes,
                minutes = document.Minutes,
            },
        };
    }

    /// <inheritdoc/>
    public async Task<object> FollowsAsync(string? window, CancellationToken cancellation)
    {
        var now = Clocks.UtcNow();
        string chosen = MachineWindows.Parse(window)?.Name ?? MachineWindows.Hour;
        double limit = MachineSampler.MemoryLimitMb;
        List<FollowsPoint> points;
        int bucketMinutes;
        bool skipped = false;
        if (chosen == MachineWindows.Hour)
        {
            bucketMinutes = 1;
            var samples = sampler.Snapshot();
            var requests = requestLog.Snapshot();
            var warm = start.At + AdminFollowsRoutes.WarmUp;
            skipped = now - TimeSpan.FromHours(1) < warm;
            points = [];
            for (var minute = now.UtcMinute.AddMinutes(-60); minute < now.UtcMinute; minute = minute.AddMinutes(1))
            {
                if (minute < warm)
                {
                    continue;
                }
                if (MachineRecorder.Fold(minute, "here", limit, samples, [], [], requests) is { } folded)
                {
                    points.Add(new FollowsPoint(folded.At, folded.Requests, folded.CpuPercent, folded.WorkingSetMb));
                }
            }
        }
        else
        {
            var (buckets, minutes) = await kept.BucketsAsync(chosen, now, cancellation);
            bucketMinutes = minutes;
            points = [.. buckets.Select(bucket => new FollowsPoint(bucket.At, bucket.Minutes == 0 ? 0 : Math.Round((double)bucket.Requests / bucket.Minutes, 2), bucket.CpuPercent, bucket.WorkingSetMb))];
            limit = buckets.Count == 0 ? limit : buckets[^1].MemoryLimitMb;
        }

        var verdict = MemoryTraffic.Judge([.. points.Select(point => new TrafficPoint(point.At, point.RequestsPerMinute, point.CpuPercent, point.WorkingSetMb))], limit);
        return new FollowsView(
            chosen,
            now,
            bucketMinutes,
            limit,
            skipped,
            points,
            new FollowsVerdictWire(CloudTones.Of(verdict.Tone), verdict.Words, verdict.MemoryFromMb, verdict.MemoryToMb, verdict.RequestsFrom, verdict.RequestsTo, verdict.ClimbThresholdMb, verdict.CpuFollowsTraffic, verdict.MemoryFollowsTraffic));
    }
}
