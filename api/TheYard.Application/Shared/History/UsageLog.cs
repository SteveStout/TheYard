// Where every Claude lane's usage is kept (ADR: Every Claude lane reports its usage): one
// document per run, keyed on the lane and the run so a document posted twice is kept once. The
// port is here; the document store's adapter is in Infrastructure.Cosmos, and this process's
// memory stands in where there is no document store.
using TheYard.Domain.Shared.Usage;

namespace TheYard.Application.Shared.History;

// #region usage-port
/// <summary>Port: where each lane's runs of Claude are kept.</summary>
public interface IUsageLog
{
    /// <summary>Whether the log can be written and read now, and one sentence about it.</summary>
    Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    /// <summary>Keeps the run under its lane and run id, replacing the copy kept before.</summary>
    Task UpsertAsync(UsageRecord record, CancellationToken cancellation);

    /// <summary>Every run that started at or after <paramref name="since"/>, in no promised order.</summary>
    Task<IReadOnlyList<UsageRecord>> SinceAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion usage-port

/// <summary>The usage log in this process's memory: the tests, and a run with no document store. It keeps a year, like the documents.</summary>
public sealed class MemoryUsageLog(string reason) : IUsageLog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UsageRecord> _records = new(StringComparer.Ordinal);

    /// <summary>The key a run is kept under: its lane and its run id.</summary>
    /// <param name="record">The run.</param>
    public static string KeyOf(UsageRecord record) => record.Lane + "|" + record.Run;

    public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new PlanHistoryAvailability(true, reason));

    public Task UpsertAsync(UsageRecord record, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _records[KeyOf(record)] = record;
            // The year the documents keep, and no more.
            var oldest = _records.Values.Max(kept => kept.At).AddDays(-366);
            foreach (string stale in _records.Where(pair => pair.Value.At < oldest).Select(pair => pair.Key).ToList())
            {
                _records.Remove(stale);
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageRecord>> SinceAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            IReadOnlyList<UsageRecord> found = [.. _records.Values.Where(record => record.At >= since)];
            return Task.FromResult(found);
        }
    }
}
