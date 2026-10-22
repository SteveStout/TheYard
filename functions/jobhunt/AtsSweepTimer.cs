using JobHunt.Functions.Ats;
using JobHunt.Functions.Merge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The ATS sweep, at 50 past every hour (UTC clock), acting only when the next hour is a records-run
/// slot in Central time, so its pass lands ten minutes before the records run's and the two read the
/// same inbox. ATS_SWEEP picks the mode: "shadow" plans and writes the plan only, "on" logs, archives
/// and reads back, anything else is off. Without the Google settings it logs why and does nothing.
/// </summary>
public sealed class AtsSweepTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<AtsSweepTimer> log)
{
    [Function("AtsSweep")]
    public async Task RunAsync([TimerTrigger("0 50 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        string mode = (Environment.GetEnvironmentVariable("ATS_SWEEP") ?? "").Trim().ToLowerInvariant();
        if (mode is not ("shadow" or "on"))
        {
            log.LogInformation("ats sweep idle: ATS_SWEEP is not shadow or on");
            return;
        }
        if (CentralClock.Slot(time.GetUtcNow().AddMinutes(10)) != CentralClock.Bookkeeping)
        {
            return;
        }
        if (GoogleCredentials.FromEnvironment() is not { } google)
        {
            log.LogWarning("ats sweep blocked: GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET or GMAIL_REFRESH_TOKEN is not set");
            return;
        }
        var inbox = new GmailInbox(http, google);
        string line = await new AtsSweep(store, inbox, time).RunAsync(mode == "on", ct);
        log.LogInformation("ats sweep: {Line}", line);
        // The leads harvest reads the same inbox in the same slot, in shadow only until it matches the records run's.
        if (string.Equals(Environment.GetEnvironmentVariable("LEADS"), "shadow", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("leads harvest: {Line}", await new Leads.LeadsHarvest(store, inbox, time).RunAsync(ct));
        }
    }
}
