namespace TheYard.Api.Shared.Observability;

// The recorded server errors behind the Admin tab's error card (ADR-010, ADR: Error handling,
// one shape everywhere): one error as it is kept, the trimmed stack a reader gets through, and
// the fixed-size ring both the server's own failures and a browser's report go into. Its own
// file beside Observability.cs because it changes when what is recorded about a failure changes,
// never when what the site asks Azure about itself changes.

/// <summary>
/// One recorded server error, newest first in snapshots. <c>Frames</c> is the
/// exception's own stack, trimmed: method names, the file each one is in and
/// the line it is on, which is the source this repository publishes anyway and
/// the fastest way from a 500 on the Admin tab to the line that threw. The
/// exception's message is still not here and still never will be: a message is
/// where a framework writes a connection detail or the value that broke a
/// constraint, and this list is public (ADR: Error handling, the addendum on
/// frames).
/// </summary>
/// <param name="At">When the error was recorded, UTC.</param>
/// <param name="Path">The request path, or the page a browser report came from.</param>
/// <param name="Status">The HTTP status code; 0 for a browser report.</param>
/// <param name="Message">A short description, such as the exception type name; never the exception's own message.</param>
/// <param name="Frames">The exception's stack, trimmed; empty when there was none.</param>
public sealed record ErrorEntry(
    DateTimeOffset At,
    string Path,
    int Status,
    string Message,
    IReadOnlyList<string> Frames);

/// <summary>An exception's stack as the Admin tab shows it: the frames, trimmed to the ones a reader gets through.</summary>
public static class StackFrames
{
    public const int Most = 12;

    public static IReadOnlyList<string> Of(Exception? exception)
    {
        if (exception?.StackTrace is not { Length: > 0 } stack)
        {
            return [];
        }

        return stack
            .Split('\n')
            .Select(line => line.Trim().TrimStart('a', 't', ' ').TrimStart())
            .Where(line => line.Length > 0)
            .Take(Most)
            .ToArray();
    }
}

/// <summary>
/// Fixed-size, thread-safe buffer of recent server errors. In-memory on
/// purpose for this demo: it resets on every roll, and the Admin tab says so.
/// </summary>
public sealed class ErrorRingBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<ErrorEntry> _entries = new();

    /// <summary>Where an entry also goes so a roll does not end it (ADR: Logs that outlive the container, the addendum on the cards); unset, nowhere.</summary>
    public Action<ErrorEntry>? Kept { get; set; }

    public void Record(string path, int status, string message, IReadOnlyList<string>? frames = null)
    {
        var entry = new ErrorEntry(DateTimeOffset.UtcNow, path, status, message, frames ?? []);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }

        Kept?.Invoke(entry);
    }

    public IReadOnlyList<ErrorEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}
