using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Sends the hiring-manager emails at 8 AM Central, seven days a week: every open lead through the campaign's
/// rules, the ones that pass ranked, and at most ten a day from the approved templates. In shadow it writes
/// what it would send and calls no mail service.
/// </summary>
// #endregion summary
/// <remarks>
/// Every hour on the hour (UTC); acts only in the 8 o'clock hour Central (CentralClock.IsCampaignHour), so the
/// November clock change moves nothing. EMAIL_CAMPAIGN picks the mode: "shadow" writes the would-sends to
/// EmailsSent with Shadow = 1, "on" sends from Steve's Gmail five seconds apart, anything else is off.
/// CAMPAIGN_SQL is the database's connection string, managed identity and no secret; without it the pass is
/// blocked and says so. A trip row in campaign/switch.log (two bounces in a Central day) holds an "on" campaign in
/// shadow. One line a pass goes to campaign/pass.log on the share. No model runs here (ADR-118).
/// </remarks>
public sealed class CampaignSendTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<CampaignSendTimer> log)
{
    [Function("CampaignSend")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var run = new RunLine("CampaignSend", now, [0]);
        try
        {
            var mode = Mode(Environment.GetEnvironmentVariable("EMAIL_CAMPAIGN"));
            if (mode == CampaignMode.Off)
            {
                run.Came("unchanged", "switch_off");
                return;
            }
            if (!CentralClock.IsCampaignHour(now))
            {
                run.Came("unchanged", "not_the_hour");
                return;
            }
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                log.LogWarning("campaign blocked: CAMPAIGN_SQL is not set");
                return;
            }
            // Two bounces in a Central day hold the campaign in shadow until a person clears the row (ADR-118, Decision 4).
            bool tripped = await store.ExistsAsync(BounceTrip.SwitchLog, ct) && BounceTrip.Tripped(TrackerCsvText.Decode(await store.ReadAsync(BounceTrip.SwitchLog, ct)));
            if (tripped && mode == CampaignMode.On)
            {
                mode = CampaignMode.Shadow;
                log.LogWarning("campaign held in shadow: {SwitchLog} says two bounces in a day", BounceTrip.SwitchLog);
            }
            IMailSender sender = mode == CampaignMode.On
                ? GoogleCredentials.FromEnvironment() is { } google ? new GmailSender(http, google) : throw new InvalidOperationException("EMAIL_CAMPAIGN is on and the Google settings are not set")
                : new NoSender();
            var database = new SqlCampaignStore(sql);
            var result = await new CampaignSend(database, database, sender, store, time).RunAsync(mode, ct);
            var local = CentralClock.Local(time.GetUtcNow());
            await store.EnsureFolderAsync("campaign", ct);
            await store.AppendAsync(CampaignSend.PassLog, TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} {result.Line}\r\n"), ct);
            log.LogInformation("campaign pass: {Line}", result.Line);
            run.Count("open", result.Open).Count("eligible", result.Eligible).Count(mode == CampaignMode.On ? "sent" : "written", result.Written)
                .Count("held", result.Held.Values.Sum()).Came(result.Came, mode == CampaignMode.On ? "on" : "shadow");
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

    /// <summary>The switch's value as a mode: "on" and "shadow", any case, and everything else off.</summary>
    public static CampaignMode Mode(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "on" => CampaignMode.On,
        "shadow" => CampaignMode.Shadow,
        _ => CampaignMode.Off,
    };

    /// <summary>The sender shadow gets: it refuses, so a shadow path that reached it would fail loudly.</summary>
    private sealed class NoSender : IMailSender
    {
        public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct) => throw new InvalidOperationException("shadow never sends");
    }
}
