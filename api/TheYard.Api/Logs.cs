// Logs that outlive the container (ADR: Logs that outlive the container).
// Three sources feed one collector: the request hook beside the request ring,
// a logging provider that takes this application's warnings and errors, and
// the same provider seeing the browser's error reports, which are logged on
// arrival. The collector writes to the document store off the request path,
// once a minute or when five hundred are waiting, and the keyed endpoint
// reads them back for the Admin tab.
//
// This file holds how an event is made and the collector that carries it to
// the store. The other parts have a file each beside it:
//   CollectorLoggerProvider.cs   the logging provider that feeds the collector
//   LogReport.cs                 the keyed endpoint's answer
//   KeptRingWriterReader.cs      the Admin tab's four public lists, kept and read back
using System.Threading.Channels;
using TheYard.Application;

namespace TheYard.Api;

// #region events
/// <summary>How a request, an exception or a log line becomes an event, and the cleaning every field gets on the way.</summary>
public static class LogEvents
{
    /// <summary>
    /// A request as an event. Its level follows the status code (5xx is an
    /// error, 4xx a warning, the rest information), and every text field is
    /// cleaned and bounded before it is kept.
    /// </summary>
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
    /// and not on the public ring.
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
    /// <summary>How many events the channel holds before the oldest is dropped.</summary>
    public const int Capacity = 10_000;

    /// <summary>Seconds between drains when configuration does not say otherwise: a minute.</summary>
    public const int DefaultIntervalSeconds = 60;

    /// <summary>How many waiting events wake the drain before its interval is up.</summary>
    private const int DrainAt = 500;

    /// <summary>The bounded queue between a request and the store; when it is full the oldest event goes.</summary>
    private readonly Channel<LogEvent> _channel = Channel.CreateBounded<LogEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest });

    /// <summary>Where drained events are written, and where the readers query them back from.</summary>
    private readonly ILogStore _store;

    /// <summary>How long the drain waits between passes.</summary>
    private readonly TimeSpan _interval;

    /// <summary>Events offered since the last drain, which is what wakes it early.</summary>
    private int _pending;

    /// <summary>Events taken into the channel since the process started.</summary>
    private long _offered;

    /// <summary>Events the store accepted since the process started.</summary>
    private long _written;

    /// <summary>Drains whose write to the store threw.</summary>
    private long _failedBatches;

    /// <summary>When the store last accepted a batch, or null before the first.</summary>
    private DateTimeOffset? _lastWrite;

    /// <summary>Completed to wake the drain early; replaced with a fresh one after every wake.</summary>
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The interval is configuration (Logs:DrainSeconds) so the browser suite can shorten it; a minute is the default and the deployed value.</summary>
    public LogCollector(ILogStore store, int intervalSeconds = DefaultIntervalSeconds)
    {
        _store = store;
        _interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 3_600));
    }

    /// <summary>The store this collector writes to, which the keyed endpoint and the card readers query.</summary>
    public ILogStore Store => _store;

    /// <summary>The time between drains, held between one second and one hour.</summary>
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

    /// <summary>
    /// The drain loop: wait for the interval or an early wake, drain, and
    /// repeat until the host stops, then drain once more on the way out.
    /// </summary>
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
