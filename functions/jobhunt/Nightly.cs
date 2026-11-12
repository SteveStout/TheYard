using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Dispatches the GitHub runs. At 2:00 AM Central: the night's picker writes the candidates file, then the jobs
/// lane. At 4:00 AM Central: the share snapshot, the night's opening bookend, then the reply lane. Every other four
/// hours: the records run, which opens and closes its own bookend.
/// </summary>
// #endregion summary
/// <remarks>
/// The job hunt's clock, every hour on the hour (UTC). At 4:00 AM Central: the share snapshot,
/// the night's opening bookend, then the reply lane. Every other four hours: the records run,
/// which opens and closes its own bookend. NIGHTLY_ANY_HOUR=on lets a hand run through as the
/// 4:00 AM slot; it is off otherwise. The 2 o'clock hour is not a slot: the picker (JobsPick) reads the postings and
/// the tracker over CAMPAIGN_SQL, or the alert leads and the share's CSV without it, writes
/// nightly/jobs-candidates-&lt;date&gt;.tsv and one line to nightly/jobs-pick.log, then the jobs lane is dispatched.
/// The 5 o'clock hour is not a slot either: when no reply run of the day wrote linkedin-queue.md, it dispatches the
/// reply lane once more (ReplyRetry), and otherwise it is the plain "not a slot" hour it always was.
/// </remarks>
public sealed class Nightly(ShareSnapshot snapshot, Bookend bookend, Dispatch dispatch, JobsHour jobs, ILogger<Nightly> log)
{
    /// <summary>One firing: the jobs hour, a slot, or nothing.</summary>
    [Function("Nightly")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new RunLine("Nightly", now, [0]);
        try
        {
            bool anyHour = string.Equals(Environment.GetEnvironmentVariable("NIGHTLY_ANY_HOUR"), "on", StringComparison.OrdinalIgnoreCase);
            if (!anyHour && CentralClock.IsJobsHour(now))
            {
                string picked = await jobs.PickAsync(ct);
                string sent = await dispatch.RunAsync(JobsFiles.Event, CentralClock.Local(now), ct);
                log.LogInformation("jobs: {Picked} | {Sent}", picked, sent);
                Dispatched(run, sent, JobsFiles.Event);
                return;
            }
            if (!anyHour && CentralClock.IsReplyRetryHour(now) && ReplyRetry.Needed(await bookend.ReplyRunsAsync(CentralClock.Local(now), ct)))
            {
                string again = await dispatch.RunAsync(CentralClock.Reply, CentralClock.Local(now), ct);
                log.LogInformation("reply retry: no linkedin-queue.md today | {Sent}", again);
                Dispatched(run, again, "reply_retry");
                return;
            }
            string? slot = anyHour ? CentralClock.Reply : CentralClock.Slot(now);
            if (slot is null)
            {
                run.Came("unchanged", "not_a_slot");
                log.LogInformation("{Now:o} is not a job hunt slot in Central time", now);
                return;
            }
            DateTime local = CentralClock.Local(now);
            if (slot == CentralClock.Reply)
            {
                string snap = await snapshot.RunAsync(now, ct);
                await bookend.OpenAsync(local, [snap], ct);
            }
            string dispatched = await dispatch.RunAsync(slot, local, ct);
            Dispatched(run, dispatched, RunLines.SlotWord(slot));
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// What a dispatch came to on the run line: success with the slot when GitHub took it (204), blocked with
    /// dispatch_refused when it did not, through Dispatch.Delivered on all three of the clock's lines
    /// (zeroresult1112: a dispatch that was not delivered recorded dispatched=0 and still came to success).
    /// A dispatch whose switch is off is unchanged, as every switched-off process is.
    /// </summary>
    public static RunLine Dispatched(RunLine run, string dispatchLine, string slot) =>
        Dispatch.Delivered(dispatchLine) ? run.Count("dispatched", 1).Came("success", slot)
        : dispatchLine.StartsWith("dispatch off", StringComparison.Ordinal) ? run.Count("dispatched", 0).Came("unchanged", "switch_off")
        : run.Count("dispatched", 0).Came("blocked", "dispatch_refused");
}
