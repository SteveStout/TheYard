using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// Dispatches the two GitHub runs. At 4:00 AM Central: the share snapshot, the night's opening bookend, then the
/// reply lane. Every other four hours: the records run, which opens and closes its own bookend.
/// </summary>
/// <remarks>
/// The job hunt's clock, every hour on the hour (UTC). At 4:00 AM Central: the share snapshot,
/// the night's opening bookend, then the reply lane. Every other four hours: the records run,
/// which opens and closes its own bookend. NIGHTLY_ANY_HOUR=on lets a hand run through as the
/// 4:00 AM slot; it is off otherwise.
/// </remarks>
public sealed class Nightly(ShareSnapshot snapshot, Bookend bookend, Dispatch dispatch, ILogger<Nightly> log)
{
    [Function("Nightly")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new RunLine("Nightly", now, [0]);
        try
        {
            bool anyHour = string.Equals(Environment.GetEnvironmentVariable("NIGHTLY_ANY_HOUR"), "on", StringComparison.OrdinalIgnoreCase);
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
