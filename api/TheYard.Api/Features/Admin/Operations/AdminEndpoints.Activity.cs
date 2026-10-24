// The Admin tab's activity routes: the public activity report, and the two reads that answer only to the
// operator's key, the visitor rows and the kept log. Each asks its one query handler (ADR-115's fourth rung);
// the key check sits beside the reads it guards and runs before the handler is asked.
using TheYard.Application.Features.Admin.Operations;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The activity routes of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers the public activity report over a window.</summary>
    private static async Task<IResult> Activity(string? window, ReadActivityHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadActivityQuery(window), cancellation));

    /// <summary>Answers the visitor rows over a window, to the operator alone and only where the rows are served; anybody else gets a 404.</summary>
    private static async Task<IResult> Visitors(string? window, HttpContext http, AdminKey adminKey, AdminSettings admin, ReadVisitorRowsHandler handler, CancellationToken cancellation) =>
        admin.VisitorRows && adminKey.IsPresentedBy(http.Request)
            ? Answer(await handler.HandleAsync(new ReadVisitorRowsQuery(window), cancellation))
            : Results.NotFound();

    /// <summary>Answers the kept log over a window, filtered, to the operator alone and only where the rows are served; anybody else gets a 404.</summary>
    private static async Task<IResult> KeptLogs(string? window, string? kind, int? status, string? path, HttpContext http, AdminKey adminKey, AdminSettings admin, ReadKeptLogsHandler handler, CancellationToken cancellation) =>
        admin.VisitorRows && adminKey.IsPresentedBy(http.Request)
            ? Answer(await handler.HandleAsync(new ReadKeptLogsQuery(window, kind, status, path), cancellation))
            : Results.NotFound();
}
