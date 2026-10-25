using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads the inbox so Steve never sees a job-board alert or a rejection again, and every human message, interview
/// and account mail stays: the ATS sweep logs receipts and rejections to the tracker and archives them; the leads
/// harvest reads every board's alert into the leads list; what neither could read is handed to the triage run; and
/// a person waiting with no draft fires the reply run (shadow: it says it would).
/// </summary>
// #endregion summary
/// <remarks>
/// The inbox pass, every 30 minutes (20 and 50 past each hour, UTC, which is 20 and 50 past in Central too):
/// the ATS sweep, then the leads harvest, then what neither could read handed to the model. Steve's 2026-10-23
/// alpha goal: he never sees a job-board alert or a rejection again, and every human message, interview and
/// account mail stays. ATS_SWEEP and LEADS each pick their mode: "shadow" plans and writes the plan only,
/// "on" acts (rows, harvest, archive, read back), anything else is off. TRIAGE "on" fires the triage run in
/// nightly.yml when the inbox holds mail from the last two days that no triage run has judged; the model
/// runs there, never here. LEADS_LIST "on" fires the leads list run when a pass harvested a lead the list has
/// not scored, and "shadow" logs what it would fire. REPLY_ON_HUMAN "on" fires the reply run when a person waits
/// on a thread with no draft (HumanMail), and "shadow" logs what it would fire. EMAIL_CAMPAIGN "shadow" or "on"
/// adds the campaign's part: delivery failures, replies and stop phrases in the threads it sent. Each part's failure is logged and the next part still runs. One line a pass goes
/// to inbox-pass/pass.log on the share with its wall time. Without the Google settings it logs why and does nothing.
/// </remarks>
public sealed class AtsSweepTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, Dispatch dispatch, ILogger<AtsSweepTimer> log)
{
    /// <summary>One line a pass, with its wall time and each part's line.</summary>
    public const string PassLog = "inbox-pass/pass.log";

    [Function("AtsSweep")]
    public async Task RunAsync([TimerTrigger("0 20,50 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var run = new RunLine("AtsSweep", time.GetUtcNow(), [20, 50]);
        try
        {
            await PassAsync(run, ct);
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

    /// <summary>
    /// One pass: each part that is switched on, its line into pass.log, and the counts into the run's line
    /// (read, archived, rows from the ATS sweep; leads and leads passed from the harvest; unjudged for the
    /// model; waiting from human mail). A pass where every switch is shadow came to planned.
    /// </summary>
    private async Task PassAsync(RunLine run, CancellationToken ct)
    {
        string ats = (Environment.GetEnvironmentVariable("ATS_SWEEP") ?? "").Trim().ToLowerInvariant();
        string leads = (Environment.GetEnvironmentVariable("LEADS") ?? "").Trim().ToLowerInvariant();
        string triage = (Environment.GetEnvironmentVariable("TRIAGE") ?? "").Trim().ToLowerInvariant();
        if (ats is not ("shadow" or "on") && leads is not ("shadow" or "on") && triage != "on")
        {
            run.Came("unchanged", "switch_off");
            log.LogInformation("inbox pass idle: ATS_SWEEP, LEADS and TRIAGE are all off");
            return;
        }
        if (GoogleCredentials.FromEnvironment() is not { } google)
        {
            run.Came("blocked", "google_settings");
            log.LogWarning("inbox pass blocked: GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET or GMAIL_REFRESH_TOKEN is not set");
            return;
        }
        var watch = Stopwatch.StartNew();
        var inbox = new GmailInbox(http, google);
        _refused = false;
        string atsLine = ats is "shadow" or "on"
            ? await PartAsync("ats sweep", () => new AtsSweep(store, inbox, time).RunAsync(ats == "on", ct))
            : "off";
        var harvest = new LeadsHarvest(store, inbox, time);
        string leadsLine = leads is "shadow" or "on"
            ? await PartAsync("leads harvest", () => harvest.RunAsync(leads == "on", ct))
            : "off";
        string list = (Environment.GetEnvironmentVariable(Dispatch.Gate(LeadsHarvest.ListEvent)) ?? "").Trim().ToLowerInvariant();
        if (list is "shadow" or "on")
        {
            leadsLine += " | list: " + await PartAsync("leads list", () => ListAsync(harvest, list == "on", ct));
        }
        string triageLine = triage == "on"
            ? await PartAsync("triage", () => TriageAsync(inbox, ct))
            : "off";
        string human = (Environment.GetEnvironmentVariable("REPLY_ON_HUMAN") ?? "").Trim().ToLowerInvariant();
        string humanLine = human is "shadow" or "on"
            ? await PartAsync("human mail", () => HumanAsync(inbox, human == "on", ct))
            : "off";
        var campaign = CampaignSendTimer.Mode(Environment.GetEnvironmentVariable("EMAIL_CAMPAIGN"));
        string? campaignSql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
        string campaignLine = campaign == CampaignMode.Off || string.IsNullOrWhiteSpace(campaignSql)
            ? "off"
            : await PartAsync("campaign", () => CampaignAsync(inbox, campaignSql, ct));
        var local = CentralClock.Local(time.GetUtcNow());
        string line = $"{local:yyyy-MM-dd HH:mm:ss} {watch.ElapsedMilliseconds} ms | ats: {atsLine} | leads: {leadsLine} | triage: {triageLine} | human: {humanLine} | campaign: {campaignLine}";
        await store.EnsureFolderAsync("inbox-pass", ct);
        await store.AppendAsync(PassLog, TrackerCsvText.Encode(line + "\r\n"), ct);
        log.LogInformation("inbox pass: {Line}", line);
        Fold(run, atsLine, leadsLine, triageLine, humanLine, _refused, ats == "on" || leads == "on" || triage == "on" || human == "on");
        RunLines.Counted(run, campaignLine, ("bounced", @"(\d+) bounced"), ("replies", @"(\d+) replies"), ("stops", @"(\d+) stops"));
        if (campaignLine.Contains("BLOCKED", StringComparison.Ordinal))
        {
            run.Came("blocked", "bounce_trip");
        }
    }

    /// <summary>
    /// The campaign's part (ADR-118, Decisions 4 and 5): the lead run's files into the database, then delivery
    /// failures every pass and replies on the pass at 20 past only, so the threads are read once an hour.
    /// </summary>
    private async Task<string> CampaignAsync(IInbox inbox, string sql, CancellationToken ct)
    {
        var database = new SqlCampaignStore(sql);
        string imported = await new LeadImport(store, database, database, new DohMxCheck(http), time).RunAsync(ct);
        string read = await new CampaignInbox(inbox, database, database, database, store, time).RunAsync(CentralClock.Local(time.GetUtcNow()).Minute < 30, ct);
        return $"{imported}; {read}";
    }

    /// <summary>The pass's counts and what it came to, from its parts' lines: numbers only, never their words.</summary>
    public static RunLine Fold(RunLine run, string atsLine, string leadsLine, string triageLine, string humanLine, bool refused, bool acted)
    {
        RunLines.Counted(run, atsLine, ("read", @"found (\d+)"), ("archived", @"(\d+) archived"));
        RunLines.Counted(run, leadsLine, ("leads", @"requisitions (\d+)"), ("leads_passed", @"pass (\d+)"));
        RunLines.Counted(run, triageLine, ("unjudged", @"(\d+) unjudged"));
        RunLines.Counted(run, humanLine, ("waiting", @"(\d+) waiting"));
        string[] parts = [atsLine, leadsLine, triageLine, humanLine];
        return refused ? run.Came("blocked", "google_token")
            : parts.Any(p => p.StartsWith("FAILED", StringComparison.Ordinal)) ? run.Came("failed", "part_failed")
            : acted ? run.Came("success")
            : run.Came("planned");
    }

    /// <summary>
    /// Fires the leads list run (LEADS-LANE.md, Sonnet at low effort) when the pass harvested a lead that passed
    /// the filter and that the list run has not scored. In shadow it says what it would fire and fires nothing.
    /// </summary>
    private async Task<string> ListAsync(LeadsHarvest harvest, bool on, CancellationToken ct)
    {
        int count = harvest.Unscored.Count;
        if (count == 0)
        {
            return "0 new, nothing to score";
        }
        return on
            ? $"{count} new, {await dispatch.RunAsync(LeadsHarvest.ListEvent, CentralClock.Local(time.GetUtcNow()), ct)}"
            : $"{count} new, shadow: would fire {LeadsHarvest.ListEvent}";
    }

    /// <summary>
    /// Fires the reply run (the nightly mode, drafts only) when a person waits on a thread with no draft, at most
    /// once every four hours between 7 AM and 9 PM Central (HumanMail); in shadow it says what it would fire.
    /// </summary>
    private async Task<string> HumanAsync(IInbox inbox, bool on, CancellationToken ct)
    {
        var human = new HumanMail(store, inbox, time);
        var (fire, waiting, why) = await human.CheckAsync(ct);
        if (!fire)
        {
            return why;
        }
        if (!on)
        {
            return $"{waiting} waiting, shadow: would fire {CentralClock.Reply} ({why})";
        }
        string fired = await dispatch.RunAsync(CentralClock.Reply, CentralClock.Local(time.GetUtcNow()), ct);
        if (fired.EndsWith(" 204", StringComparison.Ordinal))
        {
            await human.MarkFiredAsync(ct);
        }
        return $"{waiting} waiting, {fired} ({why})";
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

    /// <summary>True once Google has refused the token in this pass; the parts after it are skipped, since each would ask again.</summary>
    private bool _refused;

    /// <summary>Runs one part; a failure is logged as an error and becomes the part's line, so the next part still runs.</summary>
    private async Task<string> PartAsync(string name, Func<Task<string>> part)
    {
        if (_refused)
        {
            return "skipped: Google refused the token earlier in this pass";
        }
        try
        {
            return await part();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _refused |= ex is GoogleTokenRefusedException;
            log.LogError(ex, "inbox pass: the {Part} failed", name);
            return Describe(ex);
        }
    }

    /// <summary>
    /// A part's failure as its line in pass.log. A refused token is BLOCKED with the click that restores it,
    /// so the log and the card say what to do; anything else is the exception's type and message.
    /// </summary>
    public static string Describe(Exception ex) => ex is GoogleTokenRefusedException refused
        ? $"BLOCKED: {refused.Message}"
        : $"FAILED: {ex.GetType().Name}: {ex.Message.Replace('\n', ' ').Replace('\r', ' ')}";
}
