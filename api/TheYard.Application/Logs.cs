namespace TheYard.Application;

// Logs that outlive the container (ADR: Logs that outlive the container).
// The Admin tab's rings are this process's memory and empty on every roll;
// Application Insights keeps thirty days behind a sign-in. This port is the
// third place: one event per request, per error and per warning, written off
// the request path and kept for three years where the operator can read it back
// from the site itself.

// #region events
/// <summary>
/// One kept event. Three kinds share the shape: a <c>request</c> (method,
/// path, status, duration), an <c>error</c> (an exception or a browser
/// report, with its type, message and a bounded stack in the detail) and an
/// <c>app</c> line (a warning this application wrote). The spine is the same
/// for all three so one table can show them; what differs sits in
/// <see cref="Message"/> and <see cref="Detail"/>.
///
/// <para>Nothing here can name a person. The visitor is the daily keyed hash
/// the activity feature already uses, the network is the address cut to three
/// octets, and every string field passes through <see cref="LogText"/> before
/// it becomes an event, which bounds it and turns any at sign into its
/// percent encoding, so an address pasted into a path, a query, an error
/// message or a stack cannot be kept as one.</para>
/// </summary>
public sealed record LogEvent(
    DateTimeOffset At,
    string Kind,
    string Store,
    string Level,
    string Category,
    string Method,
    string Path,
    int Status,
    long DurationMs,
    string Visitor,
    string Network,
    string Message,
    string Detail,
    string TraceId,
    string? Entry = null,
    int? TtlSeconds = null)
{
    public const string RequestKind = "request";
    public const string ErrorKind = "error";
    public const string AppKind = "app";

    public static readonly IReadOnlyList<string> Kinds = [RequestKind, ErrorKind, AppKind];
}

// #region kept-rings
/// <summary>
/// The Admin tab's public cards, kept (ADR: Logs that outlive the container, the addendum on the cards). The
/// tab's four lists, recent errors, the log, the SQL the application ran and
/// what the document store ran, are rings in this process's memory and every
/// roll empties them. Each entry a ring takes is also kept here as one more
/// kind of event, carrying the entry exactly as the ring serves it, so a card
/// reading a day, a week or a month draws the same rows with the same code.
///
/// <para>What makes that safe to serve without a key is that it is the same
/// thing already served without one. An error entry has the exception's type
/// and frames and no message; a statement has parameter names and types and
/// nowhere to put a value; a store operation describes its partition and does
/// not name it. Nothing is added on the way to the store, and the spine's own
/// private fields (visitor, network, message, detail) are left empty.</para>
/// </summary>
public static class KeptRings
{
    public const string Errors = "ring-errors";
    public const string Log = "ring-log";
    public const string Sql = "ring-sql";
    public const string Store = "ring-store";

    /// <summary>The names a card asks by, and the kind each is kept under.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByCard = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["errors"] = Errors,
        ["logs"] = Log,
        ["sql"] = Sql,
        ["store"] = Store,
    };

    /// <summary>Thirty-five days, as the activity counters keep: the widest window a card offers is thirty, and a document must outlive the window that reads it.</summary>
    public const int RetentionSeconds = 35 * 86_400;

    /// <summary>An entry larger than this is not kept; a transactional batch is a hundred of them and has a size of its own to stay under.</summary>
    public const int MostCharacters = 16_000;

    /// <summary>One ring entry as an event: the kind, the site that took it, when, and the entry as the ring serves it.</summary>
    public static LogEvent Entry(string kind, string site, DateTimeOffset at, string json) =>
        new(at, kind, site, "", "", "", "", 0, 0, "", "", "", "", "", json, RetentionSeconds);
}

/// <summary>A window of one kept ring: the newest entries as the ring would have served them, and how many the window holds in all.</summary>
public sealed record KeptRingPage(IReadOnlyList<string> Entries, int Total);
// #endregion kept-rings

/// <summary>
/// The rule every field obeys on its way in. An at sign becomes <c>%40</c>,
/// which keeps the shape of a path readable and makes an email address
/// impossible to store as one; the length is bounded because a request line
/// can be eight kilobytes and a minified stack can be longer, and neither is
/// worth keeping whole.
/// </summary>
public static class LogText
{
    public const int MessageLength = 500;
    public const int DetailLength = 2_000;
    public const int PathLength = 200;

    public static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        // Replace first, then cut, so the bound is the bound: a cut that
        // lands inside a "%40" leaves "%4", never an at sign.
        string replaced = text.Replace("@", "%40", StringComparison.Ordinal);
        return replaced.Length > maxLength ? replaced[..maxLength] : replaced;
    }
}

/// <summary>What the operator asks the store for: a window, and optional narrowing by kind, status and a fragment of the path.</summary>
public sealed record LogQuery(DateTimeOffset Since, string? Kind, int? Status, string? PathContains, int Take);

/// <summary>Whether the store can keep events right now, and if not, why, in words a card can show.</summary>
public sealed record LogAvailability(bool Available, string Reason);

/// <summary>How many of each kind the window holds, so the card can say what a year of them costs before anyone scrolls.</summary>
public sealed record LogCount(string Kind, int Count);
// #endregion events

// #region port
/// <summary>Port: where kept events go and come back from. Writes arrive in batches, never from a request thread.</summary>
public interface ILogStore
{
    Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation);

    Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation);

    Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation);

    Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation);

    /// <summary>One kept ring for one site since a moment, newest first (ADR: Logs that outlive the container, the addendum on the cards).</summary>
    Task<KeptRingPage> RingAsync(string kind, string site, DateTimeOffset since, int take, CancellationToken cancellation);
}

/// <summary>The port wired to nothing: a container with no document store keeps no log and says so.</summary>
public sealed class NullLogStore(string reason) : ILogStore
{
    public static readonly NullLogStore Instance = new("no document store is configured, so nothing is kept");

    public Task<LogAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new LogAvailability(false, reason));

    public Task AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellation) => Task.CompletedTask;

    public Task<IReadOnlyList<LogEvent>> QueryAsync(LogQuery query, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<LogEvent>>([]);

    public Task<IReadOnlyList<LogCount>> CountAsync(DateTimeOffset since, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<LogCount>>([]);

    public Task<KeptRingPage> RingAsync(string kind, string site, DateTimeOffset since, int take, CancellationToken cancellation) =>
        Task.FromResult(new KeptRingPage([], 0));
}
// #endregion port
