// How long requests take, for the Admin tab: one request's timing, the ring of recent ones, and the
// percentiles worked out from it on read. Its own file because timing requests is a separate job
// from keeping SQL statements or log lines, and the store ring reads these percentiles too.
namespace TheYard.Api;

// #region admin-observability
/// <summary>One request, as timed by the middleware, and the store that served it (ADR: One container, both stores).</summary>
/// <param name="At">When the request was timed, UTC.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The request path.</param>
/// <param name="Status">The HTTP status code the request answered with.</param>
/// <param name="DurationMs">How long the request took, in milliseconds.</param>
/// <param name="Store">The key of the store that served the request; sql unless set.</param>
public sealed record RequestEntry(DateTimeOffset At, string Method, string Path, int Status, long DurationMs, string Store = "sql");

/// <summary>Fixed-size, thread-safe ring of recent requests and their timings.</summary>
public sealed class RequestRingBuffer(int capacity)
{
    /// <summary>
    /// The most entries the ring keeps, never below one. Zero would make the drain loop dequeue an
    /// empty queue and throw, inside a logger or an interceptor, where an exception is somebody
    /// else's bad day.
    /// </summary>
    private readonly int _capacity = Math.Max(1, capacity);

    /// <summary>The lock every read and write of the ring takes.</summary>
    private readonly object _gate = new();

    /// <summary>The entries, oldest first; the oldest leaves when a new one would pass the capacity.</summary>
    private readonly Queue<RequestEntry> _entries = new();

    /// <summary>Keeps a request's timing in the ring, dropping the oldest past the capacity.</summary>
    public void Record(RequestEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>The ring's entries, newest first, copied so the caller can read them outside the lock.</summary>
    public IReadOnlyList<RequestEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>One endpoint's timing, as the Admin tab shows it.</summary>
/// <param name="Path">The endpoint path.</param>
/// <param name="Count">How many requests to it are in the window.</param>
/// <param name="P50Ms">The median duration, in milliseconds.</param>
/// <param name="P95Ms">The 95th percentile duration, in milliseconds.</param>
/// <param name="MaxMs">The slowest duration, in milliseconds.</param>
public sealed record EndpointTiming(string Path, int Count, long P50Ms, long P95Ms, long MaxMs);

/// <summary>
/// The percentiles on the Admin tab, computed on read from the two rings.
///
/// Read, not accumulated: a running percentile needs a sketch and a sketch
/// needs a reason. These buffers hold a few hundred entries, sorting a few
/// hundred longs costs microseconds, and the number this produces is exact for
/// the window rather than approximate forever.
/// </summary>
public static class Percentiles
{
    /// <summary>
    /// The nearest-rank percentile of a window the page shows as a number, or
    /// null when the window is empty: a zero there reads as "answered in no
    /// time", and the page says "no requests" for a null instead.
    /// </summary>
    public static long? OfOrNull(IReadOnlyList<long> values, int percentile) =>
        values.Count == 0 ? null : Of(values, percentile);

    /// <summary>The nearest-rank percentile of a sample. Empty gives zero, which is why a window that can be empty reads <see cref="OfOrNull"/>.</summary>
    public static long Of(IReadOnlyList<long> values, int percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        long[] sorted = values.ToArray();
        Array.Sort(sorted);
        // Nearest rank: the smallest value at or above the given percentage of
        // the sample, which for one value is that value and for two at p95 is
        // the larger. Index arithmetic on a sorted array, no interpolation.
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    /// <summary>Per-path timings, busiest first, for the requests in a window.</summary>
    public static IReadOnlyList<EndpointTiming> ByPath(IReadOnlyList<RequestEntry> requests) =>
        requests
            .GroupBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(group =>
            {
                long[] durations = group.Select(entry => entry.DurationMs).ToArray();
                return new EndpointTiming(group.Key, durations.Length, Of(durations, 50), Of(durations, 95), durations.Max());
            })
            .OrderByDescending(timing => timing.Count)
            .ThenBy(timing => timing.Path, StringComparer.Ordinal)
            .ToArray();
}
// #endregion admin-observability
