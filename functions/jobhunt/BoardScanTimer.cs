using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads every public job board on the watchlist at 1 AM Central, once a day: the boards inbox imported first, the
/// postings seeded once, then one keyless request a company, every posting kept as a row with its history.
/// </summary>
// #endregion summary
/// <remarks>
/// Every hour on the hour (UTC); acts only in the 1 o'clock hour Central (CentralClock.IsScanHour). It needs
/// CAMPAIGN_SQL, the database's connection string, managed identity and no secret; without it the pass is blocked
/// and says so. Its line goes to campaign/pass.log in the shape the send's line had, "scan: " and the scan's counts
/// with its seconds. A scan that throws is logged and written as FAILED, and the hour ends. No model runs here.
/// </remarks>
public sealed class BoardScanTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<BoardScanTimer> log)
{
    /// <summary>One firing: in the 1 o'clock hour Central the inbox, the seed and the scan; any other hour nothing.</summary>
    [Function("BoardScan")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var run = new RunLine("BoardScan", now, [0]);
        try
        {
            if (!CentralClock.IsScanHour(now))
            {
                run.Came("unchanged", "not_the_hour");
                return;
            }
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                log.LogWarning("board scan blocked: CAMPAIGN_SQL is not set");
                return;
            }
            var database = new SqlCampaignStore(sql);
            var postings = new SqlPostingBook(sql);
            string imported = await new BoardImport(store, postings, time).RunAsync(ct);
            string seeded = await new PostingsSeed(store, postings).RunAsync(ct);
            string scan = await CampaignSendTimer.GuardedScanAsync(token => new BoardScan(database, new PublicBoardReader(http), time, postings).RunAsync(token), time, log, ct);
            var local = CentralClock.Local(time.GetUtcNow());
            await store.EnsureFolderAsync("campaign", ct);
            await store.AppendAsync(CampaignSend.PassLog, TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} scan: {scan} | boards: {imported} | {seeded}\r\n"), ct);
            log.LogInformation("board scan: {Scan} | {Imported} | {Seeded}", scan, imported, seeded);
            bool blocked = scan.Contains("postings blocked: no_postings", StringComparison.Ordinal);
            run.Came(scan.StartsWith("FAILED", StringComparison.Ordinal) ? "failed" : blocked ? "blocked" : "success", blocked ? "no_postings" : "scanned");
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
