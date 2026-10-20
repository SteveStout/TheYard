using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The 4:00 AM Central clock. Each step is its own class, run in order: the snapshot, the
/// opening bookend, then the dispatch that starts the nightly lane in GitHub Actions.
/// NIGHTLY_ANY_HOUR=on lets a hand run through the 4 o'clock check; it is off otherwise.
/// </summary>
public sealed class Nightly(ShareSnapshot snapshot, Bookend bookend, Dispatch dispatch, ILogger<Nightly> log)
{
    [Function("Nightly")]
    public async Task RunAsync([TimerTrigger("0 0 9,10 * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        bool anyHour = string.Equals(Environment.GetEnvironmentVariable("NIGHTLY_ANY_HOUR"), "on", StringComparison.OrdinalIgnoreCase);
        if (!anyHour && !CentralClock.IsFourAm(now))
        {
            log.LogInformation("not 4 AM Central at {Now:o}; the other firing does the work", now);
            return;
        }
        DateTime local = CentralClock.Local(now);
        string snap = await snapshot.RunAsync(now, ct);
        await bookend.OpenAsync(local, [snap], ct);
        await dispatch.RunAsync(local, ct);
    }
}
