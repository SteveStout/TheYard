// The public activity report, kept a short while per window so a cold read of
// every visitor row is not paid by the person opening the card. Its own file
// because it is a concurrency piece (one build per window, served stale while
// rebuilt) with tests of its own; Activity.cs lists the other parts.
namespace TheYard.Api;

// #region report
/// <summary>
/// The public report, kept a short while per window (ADR: Site activity, and the
/// line an address does not cross, addendum of 28 September). Building one reads
/// every visitor row of the window to count them, 3,661 documents for the week
/// the card opens on and 5,279 for the month when this was measured, and the
/// first read after a quiet spell paid that on top of the connections waking: 2.0
/// to 2.2 s, against about 260 ms warm. A report younger than <see cref="Fresh"/>
/// is served as it is; an older one, up to <see cref="Kept"/>, is served at once
/// while a new one is built behind it; past that, or with none yet, the caller
/// waits for the build. One build per window at a time, however many ask.
/// </summary>
public sealed class ActivityReportCache(TimeProvider clock)
{
    /// <summary>How long a report is served as it is: thirty seconds.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    /// <summary>How long an older report is still served while a new one is built behind it: ten minutes.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromMinutes(10);

    /// <summary>One built report and the instant it was built for.</summary>
    /// <param name="Report">The report as the endpoint serves it.</param>
    /// <param name="At">The instant the report was built at, which its age is measured from.</param>
    private sealed record Entry(object Report, DateTimeOffset At);

    /// <summary>The newest report per window.</summary>
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The build running per window, so callers who arrive together share one.</summary>
    private readonly Dictionary<string, Task<Entry>> _building = new(StringComparer.Ordinal);

    /// <summary>Guards both dictionaries.</summary>
    private readonly Lock _gate = new();

    /// <summary>The report for a window, built by <paramref name="build"/> at the instant it is given when one is needed.</summary>
    public async Task<object> GetAsync(string window, Func<DateTimeOffset, CancellationToken, Task<object>> build, CancellationToken cancellation)
    {
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(window, out entry);
        }

        if (entry is not null)
        {
            TimeSpan age = clock.GetUtcNow() - entry.At;
            if (age < Fresh)
            {
                return entry.Report;
            }

            if (age < Kept)
            {
                // Served now, rebuilt behind it; a failed rebuild leaves this one in place.
                _ = Build(window, build).ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
                return entry.Report;
            }
        }

        return (await Build(window, build).WaitAsync(cancellation)).Report;
    }

    /// <summary>The build for a window: the one already running, or a new one started and registered.</summary>
    private Task<Entry> Build(string window, Func<DateTimeOffset, CancellationToken, Task<object>> build)
    {
        lock (_gate)
        {
            if (!_building.TryGetValue(window, out var running))
            {
                running = BuildAsync(window, build);
                _building[window] = running;
            }

            return running;
        }
    }

    /// <summary>Builds one report, keeps it as the window's newest, and always clears the window's running build, whether the build succeeded or failed.</summary>
    private async Task<Entry> BuildAsync(string window, Func<DateTimeOffset, CancellationToken, Task<object>> build)
    {
        // Never finish inside Build's lock, so the entry for this build is in _building before it can leave.
        await Task.Yield();
        try
        {
            DateTimeOffset at = clock.GetUtcNow();
            var entry = new Entry(await build(at, CancellationToken.None), at);
            lock (_gate)
            {
                _entries[window] = entry;
            }

            return entry;
        }
        finally
        {
            lock (_gate)
            {
                _building.Remove(window);
            }
        }
    }
}
// #endregion report
