using System.Diagnostics;
using JobHunt.Functions.Ats;
using JobHunt.Functions.Merge;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The inbox pass, every 30 minutes (20 and 50 past each hour, UTC, which is 20 and 50 past in Central too):
/// the ATS sweep, then the leads harvest, then what neither could read handed to the model. Steve's 2026-10-23
/// alpha goal: he never sees a job-board alert or a rejection again, and every human message, interview and
/// account mail stays. ATS_SWEEP and LEADS each pick their mode: "shadow" plans and writes the plan only,
/// "on" acts (rows, harvest, archive, read back), anything else is off. TRIAGE "on" fires the triage run in
/// nightly.yml when the inbox holds mail from the last two days that no triage run has judged; the model
/// runs there, never here. Each part's failure is logged and the next part still runs. One line a pass goes
/// to inbox-pass/pass.log on the share with its wall time. Without the Google settings it logs why and does nothing.
/// </summary>
public sealed class AtsSweepTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, Dispatch dispatch, ILogger<AtsSweepTimer> log)
{
    /// <summary>One line a pass, with its wall time and each part's line.</summary>
    public const string PassLog = "inbox-pass/pass.log";

    [Function("AtsSweep")]
    public async Task RunAsync([TimerTrigger("0 20,50 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        string ats = (Environment.GetEnvironmentVariable("ATS_SWEEP") ?? "").Trim().ToLowerInvariant();
        string leads = (Environment.GetEnvironmentVariable("LEADS") ?? "").Trim().ToLowerInvariant();
        string triage = (Environment.GetEnvironmentVariable("TRIAGE") ?? "").Trim().ToLowerInvariant();
        if (ats is not ("shadow" or "on") && leads is not ("shadow" or "on") && triage != "on")
        {
            log.LogInformation("inbox pass idle: ATS_SWEEP, LEADS and TRIAGE are all off");
            return;
        }
        if (GoogleCredentials.FromEnvironment() is not { } google)
        {
            log.LogWarning("inbox pass blocked: GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET or GMAIL_REFRESH_TOKEN is not set");
            return;
        }
        var watch = Stopwatch.StartNew();
        var inbox = new GmailInbox(http, google);
        string atsLine = ats is "shadow" or "on"
            ? await PartAsync("ats sweep", () => new AtsSweep(store, inbox, time).RunAsync(ats == "on", ct))
            : "off";
        string leadsLine = leads is "shadow" or "on"
            ? await PartAsync("leads harvest", () => new Leads.LeadsHarvest(store, inbox, time).RunAsync(leads == "on", ct))
            : "off";
        string triageLine = triage == "on"
            ? await PartAsync("triage", () => TriageAsync(inbox, ct))
            : "off";
        var local = CentralClock.Local(time.GetUtcNow());
        string line = $"{local:yyyy-MM-dd HH:mm:ss} {watch.ElapsedMilliseconds} ms | ats: {atsLine} | leads: {leadsLine} | triage: {triageLine}";
        await store.EnsureFolderAsync("inbox-pass", ct);
        await store.AppendAsync(PassLog, TrackerCsvText.Encode(line + "\r\n"), ct);
        log.LogInformation("inbox pass: {Line}", line);
    }

    /// <summary>Fires the triage run when the inbox holds mail no triage run has judged.</summary>
    private async Task<string> TriageAsync(IInbox inbox, CancellationToken ct)
    {
        var fresh = await new InboxTriage(store, inbox).UnjudgedAsync(ct);
        if (fresh.Count == 0)
        {
            return "0 unjudged, nothing for the model";
        }
        return $"{fresh.Count} unjudged, {await dispatch.RunAsync(InboxTriage.Event, CentralClock.Local(time.GetUtcNow()), ct)}";
    }

    /// <summary>Runs one part; a failure is logged as an error and becomes the part's line, so the next part still runs.</summary>
    private async Task<string> PartAsync(string name, Func<Task<string>> part)
    {
        try
        {
            return await part();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "inbox pass: the {Part} failed", name);
            return $"FAILED: {ex.GetType().Name}: {ex.Message.Replace('\n', ' ').Replace('\r', ' ')}";
        }
    }
}
