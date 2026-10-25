using System.Text.Json.Serialization;

namespace JobHunt.Application.Features.Merge;

/// <summary>
/// Whether the tracker merger is keeping up, in counts and ages only: how long ago merge.log was last
/// written, how many lane files sit in the two spool folders, how many of them have waited more than
/// five and more than ten minutes, and the oldest wait. No file name, row or log text is in it, so it
/// can be answered to the Admin tab's nightly card without anything from the share reaching the site
/// (ADR: The tracker merger runs in Azure).
/// </summary>
/// <param name="LogAgeSeconds">Seconds since merge.log was last written; null when there is no merge.log.</param>
/// <param name="Waiting">Lane files in tracker-inbox/ and inbox/tracker-inbox/ together.</param>
/// <param name="WaitingOver5">Of those, the ones written more than five minutes ago.</param>
/// <param name="WaitingOver10">Of those, the ones written more than ten minutes ago.</param>
/// <param name="OldestWaitSeconds">Seconds the oldest waiting file has waited; null when none waits.</param>
public sealed record MergerHealth(
    [property: JsonPropertyName("log_age_s")] long? LogAgeSeconds,
    [property: JsonPropertyName("waiting")] int Waiting,
    [property: JsonPropertyName("waiting_over_5")] int WaitingOver5,
    [property: JsonPropertyName("waiting_over_10")] int WaitingOver10,
    [property: JsonPropertyName("oldest_wait_s")] long? OldestWaitSeconds)
{
    /// <summary>The two waits the card counts, in minutes.</summary>
    public const int ShownMinutes = 5, WarningMinutes = 10;

    /// <summary>Lists the two spool folders and reads merge.log's write time; reads no file's content.</summary>
    /// <param name="store">The job-hunt folder.</param>
    /// <param name="now">When the reading is taken.</param>
    /// <param name="ct">Stops the listing.</param>
    public static async Task<MergerHealth> ReadAsync(IShareReader store, DateTimeOffset now, CancellationToken ct)
    {
        var waiting = new List<MergeEntry>();
        DateTimeOffset? logWritten = null;
        foreach (string spool in MergePaths.Spools)
        {
            var listed = await store.ListFilesAsync(spool, ct);
            waiting.AddRange(listed.Where(f => TrackerMerger.IsSpoolFile(f.Name)));
            if (MergePaths.In(spool, "merge.log") == MergePaths.Log)
            {
                logWritten = listed.FirstOrDefault(f => string.Equals(f.Name, "merge.log", StringComparison.OrdinalIgnoreCase))?.LastWritten;
            }
        }
        return Measure(waiting, logWritten, now);
    }

    /// <summary>The reading from a listing already taken: the arithmetic alone, for the tests.</summary>
    /// <param name="waiting">The lane files found in the spools.</param>
    /// <param name="logWritten">When merge.log was last written; null when there is none.</param>
    /// <param name="now">When the reading is taken.</param>
    public static MergerHealth Measure(IReadOnlyList<MergeEntry> waiting, DateTimeOffset? logWritten, DateTimeOffset now)
    {
        // A clock a little ahead of the share's is a wait of nothing, never a negative one.
        long Age(DateTimeOffset at) => Math.Max(0, (long)(now - at).TotalSeconds);
        var ages = waiting.Select(f => Age(f.LastWritten)).ToList();
        return new MergerHealth(
            logWritten is { } written ? Age(written) : null,
            ages.Count,
            ages.Count(a => a > ShownMinutes * 60),
            ages.Count(a => a > WarningMinutes * 60),
            ages.Count == 0 ? null : ages.Max());
    }
}
