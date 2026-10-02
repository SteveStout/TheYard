// The log lines this application writes, kept for the Admin tab: one line's shape, the ring that
// holds the recent ones, and the logging provider that fills it. Its own file because which lines
// may reach a public page is a rule of its own, held by RingBufferLoggerProvider.
using Microsoft.Extensions.Logging.Abstractions;

namespace TheYard.Api;

// #region admin-observability
/// <summary>One log line as the Admin tab shows it.</summary>
/// <param name="At">When the line was logged, UTC.</param>
/// <param name="Level">The log level, such as Information or Warning.</param>
/// <param name="Category">The logger category that wrote the line.</param>
/// <param name="Message">The formatted message, cut to 1,000 characters.</param>
/// <param name="Exception">The exception's type name, or null when there was none.</param>
public sealed record LogEntry(DateTimeOffset At, string Level, string Category, string Message, string? Exception);

/// <summary>Fixed-size, thread-safe ring of recent log lines.</summary>
public sealed class LogRingBuffer(int capacity)
{
    /// <summary>The most entries the ring keeps, never below one.</summary>
    private readonly int _capacity = Math.Max(1, capacity);

    /// <summary>The lock every read and write of the ring takes.</summary>
    private readonly object _gate = new();

    /// <summary>The entries, oldest first; the oldest leaves when a new one would pass the capacity.</summary>
    private readonly Queue<LogEntry> _entries = new();

    /// <summary>Where a line also goes so a roll does not end it (ADR: Logs that outlive the container, the addendum on the cards); unset, nowhere.</summary>
    public Action<LogEntry>? Kept { get; set; }

    /// <summary>Keeps a line in the ring, then hands it to <see cref="Kept"/>.</summary>
    public void Record(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        Kept?.Invoke(entry);
    }

    /// <summary>The ring's entries, newest first, copied so the caller can read them outside the lock.</summary>
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
/// Admin tab shows the lines this application writes rather than a summary of
/// them.
///
/// <para>Two rules, both because the page it feeds is public.</para>
///
/// <para>It stores the formatted message and the exception's <em>type</em>,
/// never the exception's message. A database driver writes the server name, the
/// login name and the caller's IP address into an exception message.</para>
///
/// <para>And it captures only the categories <see cref="Captured"/> lists. The
/// framework's own categories are not on that list and the reason is specific:
/// on a completely healthy container, <c>Microsoft.Hosting.Lifetime</c>
/// announces the content root and <c>Microsoft.AspNetCore.DataProtection</c>
/// warns about the directory it keeps keys in. Those are server filesystem
/// paths, they are written before anything goes wrong, and nothing in this
/// application chose to publish them. An allow-list rather than a deny-list, so
/// a dependency added next year is silent here by default rather than public by
/// default (ADR: Reviewing my own work).</para>
/// </summary>
public sealed class RingBufferLoggerProvider(LogRingBuffer buffer) : ILoggerProvider
{
    /// <summary>
    /// Whose log lines reach the Admin tab: this application's own, and the one
    /// framework category the SQL section exists to show.
    /// </summary>
    public static bool Captured(string category) =>
        category.StartsWith("TheYard.", StringComparison.Ordinal)
        || category.StartsWith("TheYard.", StringComparison.Ordinal)
        || category == "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>
    /// A logger for a category: one that writes into the ring when <see cref="Captured"/> allows
    /// it, one that drops everything otherwise.
    /// </summary>
    public ILogger CreateLogger(string categoryName) =>
        Captured(categoryName) ? new RingLogger(buffer, categoryName) : NullLogger.Instance;

    /// <summary>Nothing to release: the ring belongs to the host, not to the provider.</summary>
    public void Dispose() { }

    /// <summary>The logger handed out for a captured category: writes each line into the ring.</summary>
    private sealed class RingLogger(LogRingBuffer buffer, string category) : ILogger
    {
        /// <summary>No scopes: the ring keeps the line alone.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>Information and above; debug and trace lines are not kept.</summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <summary>
        /// Writes one line into the ring: the formatted message cut to 1,000 characters, and the
        /// exception's type name, never its message.
        /// </summary>
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
// #endregion admin-observability
