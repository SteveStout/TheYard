// The runs route's guard (ADR-109, the addendum on the six processes, its second pass): the route is public,
// and each unkept page is two workspace queries through Resource Manager, whose limits the Application
// Insights card shares. A page is kept by its process, window and page the way the Cloud cards keep their
// views (ADR: One frame for every resource, the addendum that gave the runs route the same keeping): under two
// minutes old it answers at once, past one minute the next is read behind the request, and the one keeper reads
// again each page asked for in the last hour. Every fresh read, the request's, the one behind it and the
// keeper's, counts against thirty a minute; past them the route answers that it is busy and a page already kept
// stays, so no loop of addresses can spend the site's identity's query allowance.
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region guarded-process-runs
/// <summary>The runs reader with each page kept built and a cap on fresh reads a minute.</summary>
/// <param name="inner">The workspace reader.</param>
/// <param name="clock">The clock the keep and the minute are counted on.</param>
/// <param name="stopping">The host's stopping, which ends a build behind a request.</param>
public sealed class GuardedProcessRuns(IProcessRunsReader inner, TimeProvider clock, CancellationToken stopping = default) : IProcessRunsReader
{
    /// <summary>Each page as read, by process, window and page; a read that is not available is never kept.</summary>
    public KeptViews<ProcessRunsRead> Views { get; } = new("run histories", clock, stopping, read => read.Available);

    /// <summary>How many pages may be read fresh from the workspace in one minute.</summary>
    public const int FreshReadsAMinute = 30;

    private readonly object _gate = new();
    private (DateTimeOffset Minute, int Reads) _fresh;

    public Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation) =>
        Views.ReadAsync($"{process.Key}|{(long)window.TotalMinutes}|{skip}|{take}", build => FreshAsync(process, window, skip, take, build), cancellation);

    /// <summary>One page read from the workspace now, or the busy answer when this minute has had its thirty.</summary>
    private Task<ProcessRunsRead> FreshAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation) =>
        Admit(clock.GetUtcNow())
            ? inner.RunsAsync(process, window, skip, take, cancellation)
            : Task.FromResult(new ProcessRunsRead(false, "busy: the run histories were asked for more than thirty times in the last minute; ask again in a minute", [], ProcessTally.None));

    /// <summary>True, and counted, when this minute has room for one more fresh read.</summary>
    public bool Admit(DateTimeOffset now)
    {
        var minute = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, now.UtcDateTime.Day, now.UtcDateTime.Hour, now.UtcDateTime.Minute, 0, TimeSpan.Zero);
        lock (_gate)
        {
            if (_fresh.Minute != minute)
            {
                _fresh = (minute, 0);
            }
            if (_fresh.Reads >= FreshReadsAMinute)
            {
                return false;
            }
            _fresh.Reads++;
            return true;
        }
    }

    public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => inner.FoldAsync(window, cancellation);

    public string? TraceHref(string operationId) => inner.TraceHref(operationId);

    public string? ProcessHref(string process) => inner.ProcessHref(process);
}
// #endregion guarded-process-runs
