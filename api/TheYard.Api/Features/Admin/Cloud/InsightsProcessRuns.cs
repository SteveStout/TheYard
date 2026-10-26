// The job hunt Function's runs as Application Insights holds them (ADR-109, the addendum on the six processes):
// the request row the Functions host writes for each invocation, joined on its operation to the run line the
// process writes in a finally, read from the Log Analytics workspace both components send to, with the site's
// identity (Log Analytics Reader, the role the Application Insights card already reads with). One Kusto shape:
// the runs, then a page of one process, its totals, the days folded, or each process's newest.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region insights-process-runs
/// <summary>The runs reader over the workspace's AppRequests and AppTraces.</summary>
/// <param name="azure">Resource Manager, which forwards the query to the workspace.</param>
/// <param name="workspaceId">The workspace's resource id.</param>
/// <param name="role">The Function's cloud role name: its app name.</param>
public sealed class InsightsProcessRuns(AzureMetrics azure, string workspaceId, string role) : IProcessRunsReader
{
    /// <summary>
    /// Every run of the seven in a window: the request row, the run line beside it when the process wrote one, the
    /// outcome (the line's, or the request's success for a row from before the lines), whether the history lists
    /// it, and whether it did something. ItemCount is summed for the runs because a sampled row stands for more.
    /// </summary>
    public const string Runs = """
        let role = '__ROLE__';
        let since = ago(__WINDOW__);
        let lines = AppTraces
            | where TimeGenerated > since and AppRoleName == role and Message startswith 'process:'
            | summarize Line = take_any(Message) by OperationId;
        let runs = AppRequests
            | where TimeGenerated > since and AppRoleName == role and Name in (__NAMES__)
            | project At = TimeGenerated, Process = Name, OperationId, TookMs = DurationMs, Ok = tostring(Success) =~ 'true', Items = ItemCount
            | join kind=leftouter lines on OperationId
            | extend Outcome = coalesce(extract(@'outcome=([a-z]+)', 1, Line), iff(Ok, 'success', 'failed'))
            | extend Copied = Process == 'Merger' and (Line contains 'tracker_sql_parity=' or Line contains 'tracker_sql_failed=' or coalesce(toint(extract(@'tracker_sql_written=(\d+)', 1, Line)), 0) > 0)
            | extend Listed = Process in (__QUIET__) or Copied or (Outcome != 'unchanged' and (isnotempty(Line) or Outcome == 'failed'))
            | extend Acted = Outcome in ('success', 'late') and isnotempty(Line);
        """;

    /// <summary>What a set of runs adds up to, the same for a page's totals and a day.</summary>
    public const string Tally = "Runs = sum(Items), Shown = countif(Listed), Acted = countif(Acted), Late = countif(Outcome == 'late'), Failed = countif(Outcome == 'failed'), Blocked = countif(Outcome == 'blocked'), Planned = countif(Outcome == 'planned'), P95 = percentile(TookMs, 95)";

    /// <summary>One page of one process's listed runs.</summary>
    public const string Page = "runs | where Process == '__PROCESS__' and Listed | order by __ORDER__ | extend N = row_number() | where N > __SKIP__ and N <= __END__ | project At, TookMs, Ok, Line, OperationId";

    /// <summary>One process's totals for the window.</summary>
    public const string Totals = "runs | where Process == '__PROCESS__' | summarize " + Tally;

    /// <summary>Every process's runs by Central day.</summary>
    public const string Days = "runs | extend Day = format_datetime(datetime_utc_to_local(At, 'America/Chicago'), 'yyyy-MM-dd') | summarize " + Tally + " by Process, Day";

    /// <summary>Each process's newest run, and the Merger's newest pass that checked the tracker copy's parity as one more row.</summary>
    public const string Newest = "union (runs | summarize arg_max(At, TookMs, Ok, Line, OperationId) by Process), "
        + "(runs | where Process == 'Merger' and Line contains 'tracker_sql_parity=' | summarize arg_max(At, TookMs, Ok, Line, OperationId) by Process | extend Process = '" + ProcessSections.ParityKey + "')";

    /// <summary>The query with the role, the window and the seven names in place, and one statement after the runs.</summary>
    public string Kusto(TimeSpan window, string statement) => Runs
        .Replace("__ROLE__", Quote(role), StringComparison.Ordinal)
        .Replace("__WINDOW__", ((int)Math.Ceiling(window.TotalHours)).ToString(CultureInfo.InvariantCulture) + "h", StringComparison.Ordinal)
        .Replace("__NAMES__", string.Join(", ", FunctionProcesses.All.Select(p => $"'{p.Key}'")), StringComparison.Ordinal)
        .Replace("__QUIET__", string.Join(", ", FunctionProcesses.All.Where(p => p.ShowsQuietRuns).Select(p => $"'{p.Key}'")), StringComparison.Ordinal)
        + "\n" + statement;

    public async Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return new ProcessRunsRead(false, NotOnAzure, [], ProcessTally.None);
        }
        try
        {
            string page = Page.Replace("__PROCESS__", process.Key, StringComparison.Ordinal)
                .Replace("__ORDER__", process.Key == "MergerHealth" ? "TookMs desc" : "At desc", StringComparison.Ordinal)
                .Replace("__SKIP__", skip.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("__END__", (skip + take).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var rows = await azure.QueryAsync(workspaceId, Kusto(window, page), cancellation);
            var totals = await azure.QueryAsync(workspaceId, Kusto(window, Totals.Replace("__PROCESS__", process.Key, StringComparison.Ordinal)), cancellation);
            return new ProcessRunsRead(true, null, [.. rows.Select(Run)], totals.Count == 0 ? ProcessTally.None : TallyOf(totals[0]));
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // Any failed read, a refusal, a token that could not be had, a timeout or an answer not in the
            // shape asked for, is a page that says so, never this route's 500.
            return new ProcessRunsRead(false, Refused(ex), [], ProcessTally.None);
        }
    }

    public async Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation)
    {
        if (!azure.Configured)
        {
            return ProcessFold.Unread(NotOnAzure);
        }
        try
        {
            var days = await azure.QueryAsync(workspaceId, Kusto(window, Days), cancellation);
            var newest = await azure.QueryAsync(workspaceId, Kusto(window, Newest), cancellation);
            return new ProcessFold(
                true,
                null,
                [.. days.Where(row => DateOnly.TryParseExact(row.GetValueOrDefault("Day"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    .Select(row => new ProcessDay(row["Process"] ?? "?", DateOnly.ParseExact(row["Day"]!, "yyyy-MM-dd", CultureInfo.InvariantCulture), TallyOf(row)))],
                newest.Where(row => row.GetValueOrDefault("Process") is not null).ToDictionary(row => row["Process"]!, Run, StringComparer.Ordinal));
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            return ProcessFold.Unread(Refused(ex));
        }
    }

    public string? TraceHref(string operationId) => QueryLink(workspaceId,
        $"union AppRequests, AppTraces, AppDependencies, AppExceptions | where OperationId == '{Quote(operationId)}' | project TimeGenerated, Type, Name, Message, DurationMs, Success, ResultCode | order by TimeGenerated asc");

    public string? ProcessHref(string process) => QueryLink(workspaceId,
        $"AppRequests | where AppRoleName == '{Quote(role)}' and Name == '{Quote(process)}' | project TimeGenerated, Name, DurationMs, Success, OperationId | order by TimeGenerated desc");

    /// <summary>
    /// A link that opens the workspace's Logs with a query in it and runs it over thirty days: the portal's own
    /// share-a-query shape, the query gzipped, base64 and escaped.
    /// </summary>
    /// <param name="workspace">The workspace's resource id.</param>
    /// <param name="kusto">The query.</param>
    public static string QueryLink(string workspace, string kusto)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(kusto));
        }
        string query = Uri.EscapeDataString(Convert.ToBase64String(packed.ToArray()));
        return $"https://portal.azure.com/#blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/{Uri.EscapeDataString(workspace)}/source/LogsBlade.AnalyticsShareLinkToQuery/q/{query}/timespan/P30D";
    }

    /// <summary>A run from a row of the page or the newest.</summary>
    public static ProcessRun Run(IReadOnlyDictionary<string, string?> row) => ProcessRun.From(
        DateTimeOffset.TryParse(row.GetValueOrDefault("At"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToUniversalTime() : DateTimeOffset.MinValue,
        (long)(InsightsSource.Parse(row.GetValueOrDefault("TookMs")) ?? 0),
        string.Equals(row.GetValueOrDefault("Ok"), "true", StringComparison.OrdinalIgnoreCase),
        row.GetValueOrDefault("Line"),
        row.GetValueOrDefault("OperationId"));

    /// <summary>A tally from a row of the totals or the days.</summary>
    public static ProcessTally TallyOf(IReadOnlyDictionary<string, string?> row)
    {
        long Count(string name) => (long)(InsightsSource.Parse(row.GetValueOrDefault(name)) ?? 0);
        return new ProcessTally(Count("Runs"), Count("Shown"), Count("Acted"), Count("Late"), Count("Failed"), Count("Blocked"), Count("Planned"), InsightsSource.Parse(row.GetValueOrDefault("P95")));
    }

    private const string NotOnAzure = "not read here: off Azure there is no identity to ask Application Insights with";

    private static string Refused(Exception ex) => ex is HttpRequestException { StatusCode: { } code }
        ? $"Application Insights answered {(int)code}{(code == System.Net.HttpStatusCode.Forbidden ? ": the site's identity holds no reader role on the workspace" : "")}"
        : ex is HttpRequestException or TaskCanceledException ? "Application Insights did not answer"
        : $"Application Insights was not read ({ex.GetType().Name})";

    private static string Quote(string text) => text.Replace("'", "", StringComparison.Ordinal).Replace("\\", "", StringComparison.Ordinal);
}
// #endregion insights-process-runs
