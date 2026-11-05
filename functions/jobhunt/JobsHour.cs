using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The clock's work in the 2 o'clock hour Central, before it starts the jobs lane: the night's picker (JobsPick) over
/// CAMPAIGN_SQL, or over the alert leads and the share's CSV when the setting is absent, its line appended to
/// nightly/jobs-pick.log. A picker that throws is logged and answered as FAILED, and the lane is still started, so the
/// lane can say there was no candidates file.
/// </summary>
public sealed class JobsHour(IMergeStore store, TimeProvider time, ILogger<JobsHour> log)
{
    /// <summary>The picker's log on the share, one line a night.</summary>
    public const string Log = "nightly/jobs-pick.log";

    /// <summary>Runs the picker once and answers its line.</summary>
    public async Task<string> PickAsync(CancellationToken ct)
    {
        string line;
        try
        {
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            var reader = string.IsNullOrWhiteSpace(sql) ? null : new SqlJobsReader(sql);
            line = (await new JobsPick(store, reader ?? (IJobsPostings)new NoDatabase(), reader ?? (ITrackerRows)new NoDatabase(), time).RunAsync(ct)).Line;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "the night's picker failed; the jobs lane still runs");
            line = "FAILED: " + ex.GetType().Name;
        }
        await store.EnsureFolderAsync("nightly", ct);
        await store.AppendAsync(Log, TrackerCsvText.Encode($"{CentralClock.Local(time.GetUtcNow()):yyyy-MM-dd HH:mm:ss} {line}\n"), ct);
        return line;
    }

    /// <summary>No database: no postings, and a tracker copy that answers by throwing, so the picker reads the CSV.</summary>
    private sealed class NoDatabase : IJobsPostings, ITrackerRows
    {
        /// <summary>No tables.</summary>
        public Task<bool> ReadyAsync(CancellationToken ct) => Task.FromResult(false);

        /// <summary>No postings.</summary>
        public Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct) => Task.FromResult<IReadOnlyList<(long, Candidate)>>([]);

        /// <summary>No descriptions.</summary>
        public Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());

        /// <summary>No copy to read: the picker falls back to the share's CSV.</summary>
        public Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct) => throw new InvalidOperationException("CAMPAIGN_SQL is not set");
    }
}
