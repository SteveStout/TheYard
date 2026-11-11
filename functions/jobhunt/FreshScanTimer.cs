using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads every public job board on the watchlist every quarter hour for postings the table does not hold, and adds each
/// one with the instant its board says it went up, so a new posting is in SQL within fifteen minutes of going live.
/// It adds rows and writes nothing else; the 1 AM scan keeps the rest.
/// </summary>
// #endregion summary
/// <remarks>
/// At 0, 15, 30 and 45 past every hour; in the 1 o'clock hour Central it does nothing (CentralClock.IsScanHour), because
/// the daily scan reads every board then and sees the new postings itself. FRESH picks the mode: "off", any case, does
/// nothing, and anything else, unset included, runs, so a redeploy that drops the setting cannot stop it. It needs
/// CAMPAIGN_SQL and the postings tables; without either the pass is blocked and says so. Each pass appends one line to
/// campaign/fresh.log on the share, "fresh: " and the pass's counts with its seconds, on the Central clock. No model
/// runs here.
/// </remarks>
public sealed class FreshScanTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<FreshScanTimer> log)
{
    /// <summary>One firing: outside the scan's hour, with the switch not off, one fresh pass and its line.</summary>
    [Function("FreshScan")]
    public async Task RunAsync([TimerTrigger("0 */15 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var run = new RunLine("FreshScan", now, [0, 15, 30, 45]);
        try
        {
            // The same reading as FUNNEL's: only "off" stops it.
            if (FunnelTimer.Mode(Environment.GetEnvironmentVariable("FRESH")) == "off")
            {
                run.Came("unchanged", "switch_off");
                return;
            }
            if (CentralClock.IsScanHour(now))
            {
                run.Came("unchanged", "scan_hour");
                return;
            }
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                log.LogWarning("fresh pass blocked: CAMPAIGN_SQL is not set");
                return;
            }
            var postings = new SqlPostingBook(sql);
            if (!await postings.ReadyAsync(ct))
            {
                run.Came("blocked", "no_postings");
                return;
            }
            var pass = await new FreshScan(new SqlCampaignStore(sql), new PublicBoardReader(http), time, postings).RunAsync(ct);
            var local = CentralClock.Local(time.GetUtcNow());
            await store.EnsureFolderAsync("campaign", ct);
            await store.AppendAsync(FreshScan.Log, TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} fresh: {pass.Line}\r\n"), ct);
            log.LogInformation("fresh pass: {Line}", pass.Line);
            Counted(run, pass);
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

    /// <summary>The pass's counts on its run line, one name each.</summary>
    public static RunLine Counted(RunLine run, FreshPass pass) => run
        .Count("boards", pass.Boards).Count("answered", pass.Answered).Count("read", pass.Read).Count("fresh", pass.Fresh)
        .Count("failed", pass.Failed).Count("left", pass.Left).Count("took_s", pass.Seconds);
}
