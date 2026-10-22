using JobHunt.Functions.Merge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The tracker merger, every minute (UTC clock, second 0). It runs only when the MERGER setting is
/// "on", so the laptop's merger can hand over at a chosen minute and take back the same way.
/// Each pass merges whatever lanes left in the spool folders and logs to tracker-inbox/merge.log.
/// </summary>
public sealed class Merger(TrackerMerger merger, ILogger<Merger> log)
{
    [Function("Merger")]
    public async Task RunAsync([TimerTrigger("0 * * * * *")] TimerInfo timer, CancellationToken ct)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MERGER"), "on", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("tracker merger idle: MERGER is not on");
            return;
        }
        int waiting = await merger.RunPassAsync(ct);
        log.LogInformation("tracker merger pass done, {Waiting} spool file(s) found waiting", waiting);
    }
}
