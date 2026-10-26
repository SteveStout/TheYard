// What broke, for the first card under 05 What broke?: the incidents index the docs serve, read from the repository's
// own file, and the failures the Log Analytics workspace kept from every app that sends to it, read with the site's
// identity (Log Analytics Reader, the role the Application Insights card and the run histories already read with).
// A failure here is an exception or an answer of 500 or more: a 404 is the asker's mistake, and the scanners make
// hundreds of them a day. The read is kept five minutes, so a public route asks the workspace at most twelve times
// an hour whoever asks it.
using System.Globalization;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region insights-incidents
/// <summary>The incidents index from disk, and the window's failures from the workspace.</summary>
/// <param name="azure">Resource Manager, which forwards the query to the workspace.</param>
/// <param name="workspaceId">The workspace's resource id.</param>
/// <param name="repoRoot">The folder README.md sits in, where docs/incidents/ is.</param>
/// <param name="clock">The clock the read is stamped and kept by.</param>
public sealed class InsightsIncidents(AzureMetrics azure, string workspaceId, string repoRoot, TimeProvider clock) : IIncidentsReader
{
    /// <summary>How long a read of the workspace is kept.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(5);

    /// <summary>The failures: exceptions by type and operation, and answers of 500 or more by status and route, newest first.</summary>
    public const string Failures = """
        let since = ago(__WINDOW__);
        union
            (AppExceptions
                | where TimeGenerated > since
                | summarize Count = sum(ItemCount), Last = max(TimeGenerated) by Role = AppRoleName, What = ExceptionType, Where = coalesce(OperationName, Method)
                | extend Kind = 'exception'),
            (AppRequests
                | where TimeGenerated > since and toint(ResultCode) >= 500
                | summarize Count = sum(ItemCount), Last = max(TimeGenerated) by Role = AppRoleName, What = ResultCode, Where = Name
                | extend Kind = 'answer')
        | top __TOP__ by Last desc
        | project Role, Kind, What, Where, Count, Last
        """;

    private readonly object _gate = new();
    private FailuresRead? _held;

    public string IncidentsIndex()
    {
        string path = Path.Combine(repoRoot, "docs", "incidents", "INCIDENTS.md");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    public async Task<FailuresRead> FailuresAsync(TimeSpan window, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_held is { } held && now.ToUnixTimeMilliseconds() - held.ReadAtMs < Keep.TotalMilliseconds)
            {
                return held;
            }
        }
        if (!azure.Configured)
        {
            return new FailuresRead(false, "not read here: off Azure there is no identity to ask Application Insights with", now.ToUnixTimeMilliseconds(), []);
        }
        try
        {
            var rows = await azure.QueryAsync(workspaceId, Kusto(window), cancellation);
            var read = new FailuresRead(true, null, now.ToUnixTimeMilliseconds(), [.. rows.Select(Row)]);
            lock (_gate)
            {
                _held = read;
            }
            return read;
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // A refusal, a token that could not be had or a timeout is a card that says so, never this route's 500.
            string note = ex is HttpRequestException { StatusCode: { } code }
                ? $"Application Insights answered {(int)code}"
                : ex is HttpRequestException or TaskCanceledException ? "Application Insights did not answer" : $"Application Insights was not read ({ex.GetType().Name})";
            return new FailuresRead(false, note, now.ToUnixTimeMilliseconds(), []);
        }
    }

    /// <summary>The query with the window and the row limit in place.</summary>
    /// <param name="window">How far back.</param>
    public static string Kusto(TimeSpan window) => Failures
        .Replace("__WINDOW__", ((int)Math.Ceiling(window.TotalHours)).ToString(CultureInfo.InvariantCulture) + "h", StringComparison.Ordinal)
        .Replace("__TOP__", ReadIncidentsHandler.MaxFailures.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>A failure from a row of the answer.</summary>
    /// <param name="row">One row, by column name.</param>
    public static FailureRow Row(IReadOnlyDictionary<string, string?> row) => new(
        row.GetValueOrDefault("Role") ?? "",
        row.GetValueOrDefault("Kind") ?? "",
        row.GetValueOrDefault("What") ?? "",
        row.GetValueOrDefault("Where") ?? "",
        (long)(InsightsSource.Parse(row.GetValueOrDefault("Count")) ?? 0),
        DateTimeOffset.TryParse(row.GetValueOrDefault("Last"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var last) ? last.ToUnixTimeMilliseconds() : 0);
}
// #endregion insights-incidents
