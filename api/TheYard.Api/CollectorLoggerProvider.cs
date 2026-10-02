// The logging provider that turns this application's warnings and errors into kept log events.
// It has a file of its own because it plugs into the framework's logging rather than into the
// request path, and which categories it takes is a decision worth finding in one place.
// The collector it feeds is in Logs.cs (ADR: Logs that outlive the container).
using Microsoft.Extensions.Logging.Abstractions;

namespace TheYard.Api;

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
    /// <summary>
    /// The framework category that reports an unhandled exception, taken beside
    /// the ring's allow-list.
    /// </summary>
    public const string UnhandledCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    /// <summary>
    /// Whether a category's lines reach the collector: the ring's allow-list,
    /// plus the unhandled exception category.
    /// </summary>
    public static bool Captured(string category) =>
        RingBufferLoggerProvider.Captured(category) || category == UnhandledCategory;

    /// <summary>
    /// A logger that feeds the collector for a captured category, and one that
    /// discards everything for any other.
    /// </summary>
    public ILogger CreateLogger(string categoryName) =>
        Captured(categoryName) ? new CollectorLogger(collector, current, categoryName) : NullLogger.Instance;

    /// <summary>Nothing to release: the collector belongs to the host, not to this provider.</summary>
    public void Dispose() { }

    /// <summary>
    /// One category's logger. From Warning up, each line becomes an event
    /// offered to the collector, tagged with the current request's store, path
    /// and trace id.
    /// </summary>
    private sealed class CollectorLogger(LogCollector collector, Func<(string Store, string Path, string TraceId)> current, string category) : ILogger
    {
        /// <summary>Scopes are not recorded, so there is nothing to begin.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Warning and above only.</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        /// <summary>Turns one line into an event and offers it to the collector, returning at once.</summary>
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
