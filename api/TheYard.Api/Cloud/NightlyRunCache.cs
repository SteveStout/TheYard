// The nightly card's run log as the card draws it: the last thirty days read back from the kept
// log once an hour, newest first, held in this process so the card's view, which is synchronous,
// never waits on the document store (ADR: The Nightly Function on the Admin tab).
using TheYard.Data;

namespace TheYard.Api;

/// <summary>The last thirty days of nightly runs, newest first, replaced whole after each hour's read.</summary>
public sealed class NightlyRunCache
{
    private readonly object _gate = new();
    private (IReadOnlyList<NightlyRun> Runs, DateTimeOffset? At) _held = (Array.Empty<NightlyRun>(), null);

    /// <summary>The runs held, newest first; empty before the first hour is read.</summary>
    public IReadOnlyList<NightlyRun> Runs
    {
        get
        {
            lock (_gate)
            {
                return _held.Runs;
            }
        }
    }

    /// <summary>When the log was last read back; null before the first read.</summary>
    public DateTimeOffset? ReadAt
    {
        get
        {
            lock (_gate)
            {
                return _held.At;
            }
        }
    }

    /// <summary>Holds what the log answered, newest first, in place of what was held.</summary>
    /// <param name="runs">The runs read back, in any order.</param>
    /// <param name="at">When they were read.</param>
    public void Replace(IReadOnlyList<NightlyRun> runs, DateTimeOffset at)
    {
        IReadOnlyList<NightlyRun> ordered = [.. runs.OrderByDescending(run => run.At)];
        lock (_gate)
        {
            _held = (ordered, at);
        }
    }
}
