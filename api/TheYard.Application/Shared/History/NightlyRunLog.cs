// The nightly card's run log (ADR: The Nightly Function on the Admin tab): one document per
// GitHub run of the nightly workflow and one per judged slot of the Function, kept a year by the
// container's time to live. The port is here; the document store's adapter is in
// Infrastructure.Cosmos, and this process's memory stands in where there is no document store.
using TheYard.Data;

namespace TheYard.Application.Shared.History;

// #region nightly-run-port
/// <summary>Port: where the nightly workflow's runs and the Function's judged slots are kept.</summary>
public interface INightlyRunLog
{
    /// <summary>Whether the log can be written and read now, and one sentence about it.</summary>
    Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    /// <summary>Writes each run under its GitHub id, replacing the copy kept before, so a run read twice is kept once.</summary>
    Task UpsertAsync(IReadOnlyList<NightlyRun> runs, CancellationToken cancellation);

    /// <summary>Writes each judged slot under its hour, replacing the copy kept before.</summary>
    Task UpsertFiringsAsync(IReadOnlyList<NightlyFiring> firings, CancellationToken cancellation);

    /// <summary>Every run created at or after <paramref name="since"/>, in no promised order.</summary>
    Task<IReadOnlyList<NightlyRun>> SinceAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion nightly-run-port

/// <summary>The run log in this process's memory: the tests, and a run with no document store. It keeps a year, like the container.</summary>
public sealed class MemoryNightlyRunLog(string reason) : INightlyRunLog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NightlyRun> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<DateTimeOffset, NightlyFiring> _firings = new();

    public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new PlanHistoryAvailability(true, reason));

    public Task UpsertAsync(IReadOnlyList<NightlyRun> runs, CancellationToken cancellation)
    {
        lock (_gate)
        {
            foreach (var run in runs)
            {
                _runs[run.Id] = run;
            }
            if (_runs.Count > 0)
            {
                // The year the container keeps, and no more.
                var oldest = _runs.Values.Max(run => run.At).AddDays(-366);
                foreach (string stale in _runs.Values.Where(run => run.At < oldest).Select(run => run.Id).ToList())
                {
                    _runs.Remove(stale);
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task UpsertFiringsAsync(IReadOnlyList<NightlyFiring> firings, CancellationToken cancellation)
    {
        lock (_gate)
        {
            foreach (var firing in firings)
            {
                _firings[firing.Slot] = firing;
            }
            if (_firings.Count > 0)
            {
                var oldest = _firings.Keys.Max().AddDays(-366);
                foreach (var stale in _firings.Keys.Where(slot => slot < oldest).ToList())
                {
                    _firings.Remove(stale);
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NightlyRun>> SinceAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<NightlyRun>>(_runs.Values.Where(run => run.At >= since).ToList());
        }
    }
}
