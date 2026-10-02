// The keyed log endpoint's answer, shaped for the Admin tab. It has a file of its own because it
// is the read side of the kept log: it queries the store the collector writes to and decides
// what an operator sees, and none of that runs on the request path the collector protects.
using TheYard.Application;

namespace TheYard.Api;

// #region report
/// <summary>The keyed endpoint's answer: whether the store keeps anything, what the window holds by kind, the events, and what reading and writing them has cost.</summary>
public static class LogReport
{
    /// <summary>
    /// Reads one window from the store, when the store keeps anything, and
    /// shapes the answer: counts by kind, up to two hundred events matching the
    /// filter, and the collector's own counters.
    /// </summary>
    public static async Task<object> QueryAsync(LogCollector collector, string window, string? kind, int? status, string? path, DateTimeOffset now, CancellationToken cancellation)
    {
        var chosen = ActivityWindows.Parse(window)!.Value;
        DateTimeOffset since = now - chosen.Length;
        var availability = await collector.Store.AvailabilityAsync(cancellation);
        var query = new LogQuery(since, kind, status, LogText.Clean(path, 80), 200);
        IReadOnlyList<LogEvent> events = availability.Available ? await collector.Store.QueryAsync(query, cancellation) : [];
        IReadOnlyList<LogCount> counts = availability.Available ? await collector.Store.CountAsync(since, cancellation) : [];
        var counters = collector.Counters;
        return new
        {
            window = chosen.Name,
            since,
            until = now,
            kept = new { available = availability.Available, reason = availability.Reason },
            counts = LogEvent.Kinds.Select(k => new { kind = k, count = counts.FirstOrDefault(c => c.Kind == k)?.Count ?? 0 }).ToList(),
            query = new { kind = query.Kind ?? "", status = query.Status, path = query.PathContains ?? "" },
            count = events.Count,
            // Cleaned once more on the way out, so the rule holds even for a
            // document written by an older build.
            events = events.Select(e => new
            {
                at = e.At,
                kind = e.Kind,
                store = e.Store,
                level = e.Level,
                category = e.Category,
                method = e.Method,
                path = LogText.Clean(e.Path, LogText.PathLength),
                status = e.Status,
                duration_ms = e.DurationMs,
                visitor = e.Visitor,
                network = e.Network,
                message = LogText.Clean(e.Message, LogText.MessageLength),
                detail = LogText.Clean(e.Detail, LogText.DetailLength),
                trace_id = e.TraceId,
            }).ToList(),
            collector = new
            {
                offered = counters.Offered,
                written = counters.Written,
                failed_batches = counters.FailedBatches,
                last_write = counters.LastWrite,
                interval_seconds = (int)collector.Interval.TotalSeconds,
            },
        };
    }
}
// #endregion report
