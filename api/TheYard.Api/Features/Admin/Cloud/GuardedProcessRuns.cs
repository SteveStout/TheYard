// The runs route's guard (ADR-109, the addendum on the six processes, its second pass): the route is public,
// and each uncached page is two workspace queries through Resource Manager, whose limits the Application
// Insights card shares. A page is kept two minutes by its process, window and page; and past thirty uncached
// reads in a minute the route answers that it is busy rather than asking the workspace again, so no loop of
// addresses can spend the site's identity's query allowance.
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region guarded-process-runs
/// <summary>The runs reader with a two-minute keep per page and a cap on fresh reads a minute.</summary>
/// <param name="inner">The workspace reader.</param>
/// <param name="clock">The clock the keep and the minute are counted on.</param>
public sealed class GuardedProcessRuns(IProcessRunsReader inner, TimeProvider clock) : IProcessRunsReader
{
    /// <summary>How many pages may be read fresh from the workspace in one minute.</summary>
    public const int FreshReadsAMinute = 30;

    /// <summary>How long a page is kept.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);

    private readonly BoundedCache<string, ProcessRunsRead> _pages = new(1000, 16L * 1024 * 1024, Keep, read => 512 + (read.Rows.Count * 512L), clock, StringComparer.Ordinal);
    private readonly object _gate = new();
    private (DateTimeOffset Minute, int Reads) _fresh;

    public async Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
    {
        string key = $"{process.Key}|{(long)window.TotalMinutes}|{skip}|{take}";
        if (_pages.TryGet(key, out var held))
        {
            return held;
        }
        if (!Admit(clock.GetUtcNow()))
        {
            return new ProcessRunsRead(false, "busy: the run histories were asked for more than thirty times in the last minute; ask again in a minute", [], ProcessTally.None);
        }
        var read = await inner.RunsAsync(process, window, skip, take, cancellation);
        if (read.Available)
        {
            _pages.Set(key, read);
        }
        return read;
    }

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
