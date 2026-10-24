// The nightly card's run log (ADR: The Nightly Function on the Admin tab): the workflow's runs
// read from GitHub once an hour, the card's cache filled from GitHub's answer, and the run log kept
// as the durable copy when its container is there. One job, apart from the Azure reads in
// NightlySource, so it is tested with a GitHub answer and a log and nothing of Azure.
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region nightly-run-reader
/// <summary>Reads the nightly workflow's runs, keeps them, and fills the card's cache.</summary>
/// <param name="runs">The workflow's runs on GitHub.</param>
/// <param name="log">Where the runs are kept, a year.</param>
/// <param name="cache">The card's copy of the last thirty days of runs.</param>
/// <param name="clock">The clock the window is measured on.</param>
public sealed class NightlyRunReader(GitHubRuns runs, INightlyRunLog log, NightlyRunCache cache, TimeProvider clock)
{
    /// <summary>The card's copy of the run log.</summary>
    public NightlyRunCache Cache => cache;

    /// <summary>
    /// Reads the workflow's runs and fills the cache from GitHub's answer; keeps them in the run log
    /// when it is there and folds in the older runs it holds. Says in a few words how the read went.
    /// </summary>
    public async Task<string> ReadAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        bool kept = (await log.AvailabilityAsync(cancellation)).Available;
        IReadOnlyList<NightlyRun> listed;
        try
        {
            listed = await runs.ReadAsync(now, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // Any failure but the host stopping: a refusal, the client's own timeout, a body that is
            // not the shape asked for. The card falls back to the log and says which.
            if (kept)
            {
                cache.Replace(await log.SinceAsync(now - GitHubRuns.Window, cancellation), now);
            }
            return ex is HttpRequestException { StatusCode: { } code } ? $"not read: GitHub answered {(int)code}"
                : ex is HttpRequestException ? "not read: GitHub did not answer"
                : $"not read: GitHub did not answer ({ex.GetType().Name})";
        }
        if (!kept)
        {
            cache.Replace(listed, now);
            return $"{listed.Count} runs read";
        }
        await log.UpsertAsync(listed, cancellation);
        var fresh = listed.Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        var older = (await log.SinceAsync(now - GitHubRuns.Window, cancellation)).Where(run => !fresh.Contains(run.Id));
        cache.Replace([.. listed, .. older], now);
        return $"{listed.Count} runs read";
    }
}
// #endregion nightly-run-reader
