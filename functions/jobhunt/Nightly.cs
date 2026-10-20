using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The job hunt's clock, every hour on the hour (UTC). At 4:00 AM Central: the share snapshot,
/// the night's opening bookend, then the reply lane. Every other four hours: the records run,
/// which opens and closes its own bookend. NIGHTLY_ANY_HOUR=on lets a hand run through as the
/// 4:00 AM slot; it is off otherwise.
/// </summary>
public sealed class Nightly(ShareSnapshot snapshot, Bookend bookend, Dispatch dispatch, ILogger<Nightly> log)
{
    [Function("Nightly")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        bool anyHour = string.Equals(Environment.GetEnvironmentVariable("NIGHTLY_ANY_HOUR"), "on", StringComparison.OrdinalIgnoreCase);
        string? slot = anyHour ? CentralClock.Reply : CentralClock.Slot(now);
        if (slot is null)
        {
            log.LogInformation("{Now:o} is not a job hunt slot in Central time", now);
            return;
        }
        DateTime local = CentralClock.Local(now);
        if (slot == CentralClock.Reply)
        {
            string snap = await snapshot.RunAsync(now, ct);
            await bookend.OpenAsync(local, [snap], ct);
        }
        await dispatch.RunAsync(slot, local, ct);
    }
}
