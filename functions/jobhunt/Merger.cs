using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Merges whatever the lanes left in the spool folders into the tracker, row fragments and text appends alike, and
/// logs each pass to tracker-inbox/merge.log. It runs only while its switch is on, so the laptop's merger could
/// hand over at a chosen minute and take back the same way; the laptop's was unregistered on 23 Oct.
/// </summary>
// #endregion summary
/// <remarks>
/// The tracker merger, every minute (UTC clock, second 0). It runs only when the MERGER setting is
/// "on", so the laptop's merger can hand over at a chosen minute and take back the same way.
/// Each pass merges whatever lanes left in the spool folders and logs to tracker-inbox/merge.log.
/// </remarks>
public sealed class Merger(TrackerMerger merger, TimeProvider time, ILogger<Merger> log)
{
    [Function("Merger")]
    public async Task RunAsync([TimerTrigger("0 * * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var run = new RunLine("Merger", time.GetUtcNow());
        try
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("MERGER"), "on", StringComparison.OrdinalIgnoreCase))
            {
                run.Came("unchanged", "switch_off");
                log.LogInformation("tracker merger idle: MERGER is not on");
                return;
            }
            // APPENDS=on makes this pass the one writer of the shared .md files as well; off, the laptop's
            // merger keeps them (ADR: The tracker merger runs in Azure, the addendum on the appends).
            bool appends = string.Equals(Environment.GetEnvironmentVariable("APPENDS"), "on", StringComparison.OrdinalIgnoreCase);
            int waiting = await merger.RunPassAsync(ct, appends);
            run.Count("merged", waiting).Came(waiting == 0 ? "unchanged" : "success");
            log.LogInformation("tracker merger pass done, {Waiting} spool file(s) found waiting, appends {Appends}", waiting, appends ? "on" : "off");
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, time.GetUtcNow());
        }
    }
}
