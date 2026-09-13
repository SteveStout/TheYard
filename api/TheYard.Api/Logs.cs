using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Application;

namespace TheYard.Api;

// Logs that outlive the container (ADR: Logs that outlive the container).
// Three sources feed one collector: the request hook beside the request ring,
// a logging provider that takes this application's warnings and errors, and
// the same provider seeing the browser's error reports, which are logged on
// arrival. The collector writes to the document store off the request path,
// once a minute or when five hundred are waiting, and the keyed endpoint
// reads them back for the Admin tab.

// #region events
/// <summary>How a request, an exception or a log line becomes an event, and the cleaning every field gets on the way.</summary>
public static class LogEvents
{
    public static LogEvent Request(DateTimeOffset at, string method, string path, int status, long durationMs, string store, string visitor, string network, string traceId) =>
        new(
            at,
            LogEvent.RequestKind,
            store,
            status >= 500 ? "Error" : status >= 400 ? "Warning" : "Information",
            "request",
            LogText.Clean(method, 16),
            LogText.Clean(path, LogText.PathLength),
            status,
            durationMs,
            visitor,
            network,
            "",
            "",
            LogText.Clean(traceId, 64));

    /// <summary>
    /// A line from a logger. Error and above is an error event; anything
    /// below is an app event. The exception's type, message and stack go in
    /// the detail, bounded and cleaned like everything else: this log is
    /// behind the operator's key, which is why the message can be kept here
    /// and not on the public ring (the staff review, 2026-09-03).
    /// </summary>
    public static LogEvent Line(DateTimeOffset at, LogLevel level, string category, string message, Exception? exception, string store, string path, string traceId)
    {
        string detail = exception is null
            ? ""
            : exception.GetType().Name + ": " + exception.Message + (exception.StackTrace is null ? "" : "\n" + exception.StackTrace);
        return new LogEvent(
            at,
            level >= LogLevel.Error ? LogEvent.ErrorKind : LogEvent.AppKind,
            store,
            level.ToString(),
            LogText.Clean(category, 120),
            "",
            LogText.Clean(path, LogText.PathLength),
            0,
            0,
            "",
            "",
            LogText.Clean(message, LogText.MessageLength),
            LogText.Clean(detail, LogText.DetailLength),
            LogText.Clean(traceId, 64));
    }
}
// #endregion events

// #region collector
/// <summary>
/// Events off the request path. The same shape as the activity collector and
/// for the same reasons: a request offers its event to a bounded channel and
/// leaves, this service drains the channel on its own clock, and a full
/// channel drops the oldest event rather than blocking anybody. The clock is
/// a minute rather than five seconds because a log write is a create and
/// never a merge, so nothing is gained by hurrying, and a document store
/// that is written once a minute is not being kept busy by its own log.
/// </summary>
public sealed class LogCollector : BackgroundService
{
    public const int Capacity = 10_000;
    public const int DefaultIntervalSeconds = 60;
    private const int DrainAt = 500;

    private readonly Channel<LogEvent> _channel = Channel.CreateBounded<LogEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly ILogStore _store;
    private readonly TimeSpan _interval;
    private int _pending;
    private long _offered;
    private long _written;
    private long _failedBatches;
    private DateTimeOffset? _lastWrite;
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The interval is configuration (Logs:DrainSeconds) so the browser suite can shorten it; a minute is the default and the deployed value.</summary>
    public LogCollector(ILogStore store, int intervalSeconds = DefaultIntervalSeconds)
    {
        _store = store;
        _interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 3_600));
    }

    public ILogStore Store => _store;

    public TimeSpan Interval => _interval;

    /// <summary>What has passed through: offered, written, batches that failed, and the last time anything was written.</summary>
    public (long Offered, long Written, long FailedBatches, DateTimeOffset? LastWrite) Counters =>
        (Interlocked.Read(ref _offered), Interlocked.Read(ref _written), Interlocked.Read(ref _failedBatches), _lastWrite);

    /// <summary>Called on the request thread, or inside a logger, and returns at once.</summary>
    public void Offer(LogEvent e)
    {
        if (_channel.Writer.TryWrite(e))
        {
            Interlocked.Increment(ref _offered);
            if (Interlocked.Increment(ref _pending) >= DrainAt)
            {
                _wake.TrySetResult();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAny(Task.Delay(_interval, stopping), _wake.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await DrainAsync(CancellationToken.None);
        }

        // Whatever is left goes out with the process.
        await DrainAsync(CancellationToken.None);
    }

    /// <summary>One pass: everything queued right now, in one call to the store.</summary>
    public async Task DrainAsync(CancellationToken cancellation)
    {
        var batch = new List<LogEvent>();
        while (_channel.Reader.TryRead(out var e))
        {
            batch.Add(e);
        }

        Interlocked.Exchange(ref _pending, 0);
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await _store.AppendAsync(batch, cancellation);
            Interlocked.Add(ref _written, batch.Count);
            _lastWrite = DateTimeOffset.UtcNow;
        }
        catch (Exception)
        {
            // Counted, never logged: a log line about the log failing would
            // arrive back here.
            Interlocked.Increment(ref _failedBatches);
        }
    }
}
// #endregion collector

// #region provider
/// <summary>
/// The logging provider that feeds the collector. The same allow-list of
/// categories as the Admin tab's ring, plus the one framework category that
/// reports an unhandled exception, and only from Warning up: an Information
/// line is the request log's job, and the request hook already keeps one
/// event per request. Which store and which request a line belongs to are
/// read from the current request when there is one.
/// </summary>
public sealed class CollectorLoggerProvider(LogCollector collector, Func<(string Store, string Path, string TraceId)> current) : ILoggerProvider
{
    public const string UnhandledCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    public static bool Captured(string category) =>
        RingBufferLoggerProvider.Captured(category) || category == UnhandledCategory;

    public ILogger CreateLogger(string categoryName) =>
        Captured(categoryName) ? new CollectorLogger(collector, current, categoryName) : NullLogger.Instance;

    public void Dispose() { }

    private sealed class CollectorLogger(LogCollector collector, Func<(string Store, string Path, string TraceId)> current, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var (store, path, traceId) = current();
            collector.Offer(LogEvents.Line(DateTimeOffset.UtcNow, logLevel, category, formatter(state, exception!), exception, store, path, traceId));
        }
    }
}
// #endregion provider

// #region report
/// <summary>The keyed endpoint's answer: whether the store keeps anything, what the window holds by kind, the events, and what reading and writing them has cost.</summary>
public static class LogReport
{
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
