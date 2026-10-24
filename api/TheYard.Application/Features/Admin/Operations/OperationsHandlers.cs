// The Admin tab's reads of its own rings and its activity, one query and one handler each (ADR-115's fourth
// rung). A handler decides what the route used to decide in its body: which window, card or kind it can read, and
// what to answer when there is nothing; the port answers the reading itself. The machines, Azure, the request and
// the evidence are in EvidenceHandlers.cs.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Admin.Operations;

// #region ring-handlers
/// <summary>The SQL ring.</summary>
/// <param name="rings">The process's rings.</param>
public sealed class ReadSqlRingHandler(IRingReadings rings) : IQueryHandler<ReadSqlRingQuery, Reading>
{
    /// <summary>The ring as it stands.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadSqlRingQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(rings.Sql()));
}

/// <summary>Asks for the SQL ring.</summary>
public sealed record ReadSqlRingQuery;

/// <summary>The document store's operation ring.</summary>
/// <param name="rings">The process's rings.</param>
public sealed class ReadStoreRingHandler(IRingReadings rings) : IQueryHandler<ReadStoreRingQuery, Reading>
{
    /// <summary>The ring as it stands, with the store's name.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadStoreRingQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(rings.Store()));
}

/// <summary>Asks for the store operation ring.</summary>
public sealed record ReadStoreRingQuery;

/// <summary>The log ring.</summary>
/// <param name="rings">The process's rings.</param>
public sealed class ReadLogRingHandler(IRingReadings rings) : IQueryHandler<ReadLogRingQuery, Reading>
{
    /// <summary>The ring as it stands.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadLogRingQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(rings.Logs()));
}

/// <summary>Asks for the log ring.</summary>
public sealed record ReadLogRingQuery;

/// <summary>The timings built from the request and store rings.</summary>
/// <param name="rings">The process's rings.</param>
public sealed class ReadMetricsHandler(IRingReadings rings) : IQueryHandler<ReadMetricsQuery, Reading>
{
    /// <summary>The timings as they stand.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadMetricsQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(rings.Metrics()));
}

/// <summary>Asks for the timings.</summary>
public sealed record ReadMetricsQuery;

/// <summary>One card's kept ring over a window; a card or a window it does not keep is refused.</summary>
/// <param name="kept">The kept rings.</param>
public sealed class ReadKeptRingHandler(IKeptRingReadings kept) : IQueryHandler<ReadKeptRingQuery, Reading>
{
    /// <summary>The kept ring, or the refusal.</summary>
    /// <param name="query">The card and the window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadKeptRingQuery query, CancellationToken cancellation) =>
        await kept.ReadAsync(query.Card, query.Window, cancellation) is { } answer
            ? Reading.Of(answer)
            : new Reading(ReadingKind.Refused, Title: "The card or the window could not be read", Detail: "card is one of errors, logs, sql or store, and window is one of 24h, 7d or 30d.");
}

/// <summary>Asks for one card's kept ring.</summary>
/// <param name="Card">errors, logs, sql or store.</param>
/// <param name="Window">24h, 7d or 30d.</param>
public sealed record ReadKeptRingQuery(string? Card, string? Window);
// #endregion ring-handlers

// #region activity-handlers
/// <summary>The public activity report over a window.</summary>
/// <param name="activity">Who came.</param>
public sealed class ReadActivityHandler(IActivityReadings activity) : IQueryHandler<ReadActivityQuery, Reading>
{
    /// <summary>The report, or the refusal of a window it does not read.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadActivityQuery query, CancellationToken cancellation) =>
        Reading.WindowReads(query.Window) ? Reading.Of(await activity.PublicAsync(query.Window ?? "24h", cancellation)) : Reading.BadWindow();
}

/// <summary>Asks for the activity report.</summary>
/// <param name="Window">24h, 7d or 30d; 24h when absent.</param>
public sealed record ReadActivityQuery(string? Window);

/// <summary>The visitor rows over a window, for the operator alone; the route checks the key before it asks.</summary>
/// <param name="activity">Who came.</param>
public sealed class ReadVisitorRowsHandler(IActivityReadings activity) : IQueryHandler<ReadVisitorRowsQuery, Reading>
{
    /// <summary>The rows, or the refusal of a window it does not read.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadVisitorRowsQuery query, CancellationToken cancellation) =>
        Reading.WindowReads(query.Window) ? Reading.Of(await activity.VisitorsAsync(query.Window ?? "24h", cancellation)) : Reading.BadWindow();
}

/// <summary>Asks for the visitor rows.</summary>
/// <param name="Window">24h, 7d or 30d; 24h when absent.</param>
public sealed record ReadVisitorRowsQuery(string? Window);

/// <summary>The kept log over a window, filtered, for the operator alone; the route checks the key before it asks.</summary>
/// <param name="activity">Who came.</param>
public sealed class ReadKeptLogsHandler(IActivityReadings activity) : IQueryHandler<ReadKeptLogsQuery, Reading>
{
    /// <summary>The log, or the refusal of a window or a kind it does not read.</summary>
    /// <param name="query">The window and the filters.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadKeptLogsQuery query, CancellationToken cancellation)
    {
        if (!Reading.WindowReads(query.Window))
        {
            return Reading.BadWindow();
        }
        return await activity.KeptLogsAsync(query.Window ?? "24h", query.Kind, query.Status, query.Path, cancellation) is { } log
            ? Reading.Of(log)
            : new Reading(ReadingKind.Refused, Title: "The kind could not be read", Detail: "kind is one of request, error or app.");
    }
}

/// <summary>Asks for the kept log.</summary>
/// <param name="Window">24h, 7d or 30d; 24h when absent.</param>
/// <param name="Kind">request, error or app; any when absent.</param>
/// <param name="Status">One status code, or any.</param>
/// <param name="Path">Part of a path, or any.</param>
public sealed record ReadKeptLogsQuery(string? Window, string? Kind, int? Status, string? Path);
// #endregion activity-handlers
