using TheBlock.Application;

namespace TheBlock.Api;

// #region admin-observability
// What the running system is doing, on the Admin tab: the SQL it sends, the
// log lines it writes, and how long both take (ADR: What the database is
// actually doing).
//
// All three are the same shape as the error buffer that was already here: a
// fixed-size ring in this process's memory, reset by every container roll, and
// the page says so. A demo does not need a log store, and a log store is
// exactly the kind of thing that turns a free tier into a bill.

/// <summary>
/// Fixed-size, thread-safe ring of recent SQL statements. Nothing here holds a
/// parameter value: <see cref="SqlStatement"/> has no field for one.
/// </summary>
public sealed class SqlRingBuffer(int capacity) : ISqlLog
{
    private readonly object _gate = new();
    private readonly Queue<SqlStatement> _entries = new();

    public void Record(SqlStatement statement)
    {
        lock (_gate)
        {
            _entries.Enqueue(statement);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<SqlStatement> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>One log line as the Admin tab shows it.</summary>
public sealed record LogEntry(DateTimeOffset At, string Level, string Category, string Message, string? Exception);

/// <summary>Fixed-size, thread-safe ring of recent log lines.</summary>
public sealed class LogRingBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries = new();

    public void Record(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>
/// A logging provider that writes into <see cref="LogRingBuffer"/>, so the
/// Admin tab shows the same lines the console does rather than a summary of
/// them. It stores the formatted message and the exception's type, never the
/// exception's message: a provider's exception text can quote the value that
/// broke a constraint, and this page is public.
/// </summary>
public sealed class RingBufferLoggerProvider(LogRingBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RingLogger(buffer, categoryName);

    public void Dispose() { }

    private sealed class RingLogger(LogRingBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception!);
            buffer.Record(new LogEntry(
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                category,
                message.Length > 1_000 ? message[..1_000] + "..." : message,
                exception?.GetType().Name));
        }
    }
}

/// <summary>One request, as timed by the middleware.</summary>
public sealed record RequestEntry(DateTimeOffset At, string Method, string Path, int Status, long DurationMs);

/// <summary>Fixed-size, thread-safe ring of recent requests and their timings.</summary>
public sealed class RequestRingBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<RequestEntry> _entries = new();

    public void Record(RequestEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<RequestEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>Answers <see cref="ICurrentRequest"/> from the ambient HttpContext.</summary>
public sealed class HttpCurrentRequest(IHttpContextAccessor accessor) : ICurrentRequest
{
    public string? Describe()
    {
        var context = accessor.HttpContext;
        if (context is null)
        {
            return null;
        }

        string query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value! : "";
        return $"{context.Request.Method} {context.Request.Path}{query}";
    }
}

/// <summary>One endpoint's timing, as the Admin tab shows it.</summary>
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
    /// <summary>The nearest-rank percentile of a sample. Empty gives zero.</summary>
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
