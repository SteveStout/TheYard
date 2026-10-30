using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Merges whatever the lanes left in the spool folders into the tracker, row fragments and text appends alike, and
/// logs each pass to tracker-inbox/merge.log. It runs only while its switch is on, which is how it and the
/// laptop's merger hand the job over at a chosen minute and take it back the same way: two mergers writing
/// the same tracker would append every fragment twice, so exactly one of them is on at any minute.
/// </summary>
// #endregion summary
/// <remarks>
/// The tracker merger, every minute (UTC clock, second 0). It runs only when the MERGER setting is
/// "on", so the laptop's merger can hand over at a chosen minute and take back the same way.
/// Each pass merges whatever lanes left in the spool folders and logs to tracker-inbox/merge.log.
/// </remarks>
public sealed class Merger(TrackerMerger merger, IMergeStore store, TrackerSeen seen, TimeProvider time, ILogger<Merger> log)
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
            await CopyAsync(run, ct);
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

    // #region tracker-copy
    /// <summary>
    /// The tracker's SQL copy, after the CSV is written (PROMPT-JOBHUNT-TRACKER-SQL): TRACKER_SQL=import copies the file
    /// once, when the copy is empty, and checks it byte for byte; on copies the rows that changed after every pass and
    /// checks the whole file on the first pass of each Central day. The CSV stays the master: a copy that fails is
    /// logged and the next pass tries again, and nothing here writes the CSV. The database is the campaign's
    /// connection, the site's database.
    /// </summary>
    private async Task CopyAsync(RunLine run, CancellationToken ct)
    {
        string mode = (Environment.GetEnvironmentVariable("TRACKER_SQL") ?? "").Trim().ToLowerInvariant();
        string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
        if (mode is not ("import" or "on") || string.IsNullOrWhiteSpace(sql))
        {
            return;
        }
        try
        {
            var table = new SqlTrackerTable(sql, time);
            var mirror = new TrackerMirror(store, table, seen);
            var now = time.GetUtcNow();
            var frame = await table.FrameAsync(ct);
            if (mode == "import" && frame is not null)
            {
                return;
            }
            var copied = await mirror.CopyAsync(ct);
            run.Count("tracker_sql_rows", copied.Rows).Count("tracker_sql_written", copied.Written);
            if (TrackerMirror.ParityDue(frame?.ParityAt, now))
            {
                var parity = await mirror.ParityAsync(now, ct);
                run.Count("tracker_sql_parity", parity.Same ? 1 : 0).Count("tracker_sql_csv_rows", parity.CsvRows).Count("tracker_sql_sql_rows", parity.SqlRows);
                log.LogInformation("tracker SQL parity: {Same}, CSV {Csv} rows, SQL {Sql} rows", parity.Same ? "same" : "DIFFERENT", parity.CsvRows, parity.SqlRows);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The CSV is the master: a copy that did not land is the next pass's, and the run line says so.
            run.Count("tracker_sql_failed", 1);
            log.LogWarning(ex, "tracker SQL copy failed; the next pass tries again");
        }
    }
    // #endregion tracker-copy
}
