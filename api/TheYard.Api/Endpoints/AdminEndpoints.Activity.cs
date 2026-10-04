// The Admin tab's activity handlers: the public activity report, and the two reads that
// answer only to the operator's key, the visitor rows and the kept log. Its own file because
// these are the reads about who came, and the key check sits beside the reads it guards.

using TheYard.Application;

namespace TheYard.Api;

/// <summary>The activity handlers of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>
    /// Answers the public activity report for a named window from the report cache, or a 400 when the window is not
    /// 24h, 7d or 30d.
    /// </summary>
    private static async Task<IResult> Activity(string? window, ActivityCollector collector, ActivityReportCache activityReports, Backends backends, AdminSettings admin, CancellationToken cancellation) =>
        ActivityWindows.Parse(window) is null
            ? Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read")
            : Results.Json(await activityReports.GetAsync(
                window ?? "24h",
                (now, building) => ActivityReport.PublicAsync(collector, backends, window ?? "24h", now, admin.VisitorRows, building),
                cancellation));

    /// <summary>
    /// Answers the visitor rows for a window, only to the operator's key; anything else is a 404, so a stranger
    /// cannot tell the route exists.
    /// </summary>
    private static async Task<IResult> Visitors(string? window, HttpContext http, ActivityCollector collector, Backends backends, AdminKey adminKey, AdminSettings admin, CancellationToken cancellation)
    {
        if (!admin.VisitorRows || !adminKey.IsPresentedBy(http.Request))
        {
            return Results.NotFound();
        }

        return ActivityWindows.Parse(window) is null
            ? Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read")
            : Results.Json(await ActivityReport.VisitorsAsync(collector, backends, window ?? "24h", DateTimeOffset.UtcNow, cancellation));
    }

    /// <summary>
    /// Answers the kept log for a window, filtered by kind, status and a fragment of the path, only to the
    /// operator's key.
    /// </summary>
    private static async Task<IResult> KeptLogs(string? window, string? kind, int? status, string? path, HttpContext http, LogCollector collector, AdminKey adminKey, AdminSettings admin, CancellationToken cancellation)
    {
        if (!admin.VisitorRows || !adminKey.IsPresentedBy(http.Request))
        {
            return Results.NotFound();
        }

        if (ActivityWindows.Parse(window) is null)
        {
            return Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read");
        }

        if (!string.IsNullOrEmpty(kind) && !LogEvent.Kinds.Contains(kind, StringComparer.Ordinal))
        {
            return Results.Problem(detail: "kind is one of request, error or app.", statusCode: 400, title: "The kind could not be read");
        }

        return Results.Json(await LogReport.QueryAsync(collector, window ?? "24h", kind, status, path, DateTimeOffset.UtcNow, cancellation));
    }
}
