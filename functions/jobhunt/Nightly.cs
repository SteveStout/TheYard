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
                run.Count("dispatched", Dispatch.Delivered(sent) ? 1 : 0).Came("success", JobsFiles.Event);
                return;
            }
            if (!anyHour && CentralClock.IsReplyRetryHour(now) && ReplyRetry.Needed(await bookend.ReplyRunsAsync(CentralClock.Local(now), ct)))
            {
                string again = await dispatch.RunAsync(CentralClock.Reply, CentralClock.Local(now), ct);
                log.LogInformation("reply retry: no linkedin-queue.md today | {Sent}", again);
                run.Count("dispatched", Dispatch.Delivered(again) ? 1 : 0).Came("success", "reply_retry");
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
            run.Count("dispatched", dispatched.EndsWith(" 204", StringComparison.Ordinal) ? 1 : 0).Came("success", RunLines.SlotWord(slot));
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
}
