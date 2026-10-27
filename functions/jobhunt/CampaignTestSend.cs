using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Proves the campaign's real send path on demand: one open lead with an address, rendered from its approved
/// template and sent through the Function's own Gmail sender, to the owner's test address only.
/// </summary>
// #endregion summary
/// <remarks>
/// POST /api/campaign/test-send with the function key. The address comes from the app setting CAMPAIGN_TEST_TO and
/// never from the lead: the lead's own address is not read for sending, so no hiring manager is written to. The
/// subject is marked [TEST], the five links carry the code tst001 in place of the lead's code so the opens report is
/// not fed a false open, nothing is written to EmailsSent, and the 30-day rule and the cap are not spent. With the
/// setting absent the call answers 409 and sends nothing. One line a call goes to campaign/test.log on the share.
/// </remarks>
public sealed class CampaignTestSend(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<CampaignTestSend> log)
{
    /// <summary>The app setting that holds the one address a test may go to.</summary>
    public const string Setting = "CAMPAIGN_TEST_TO";

    /// <summary>The share's log of test sends.</summary>
    public const string Log = "campaign/test.log";

    /// <summary>The code the test's links carry in place of the lead's.</summary>
    public const string TestCode = "tst001";

    [Function("CampaignTestSend")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "campaign/test-send")] HttpRequestData request,
        CancellationToken ct)
    {
        var run = new RunLine("CampaignTestSend", time.GetUtcNow());
        try
        {
            string? to = Environment.GetEnvironmentVariable(Setting);
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_test_address");
                return await AnswerAsync(request, HttpStatusCode.Conflict, $"{Setting} or CAMPAIGN_SQL is not set: nothing sent", ct);
            }
            if (!OneAddress(to))
            {
                run.Came("blocked", "not_one_address");
                return await AnswerAsync(request, HttpStatusCode.Conflict, $"{Setting} is not one address: nothing sent", ct);
            }
            if (GoogleCredentials.FromEnvironment() is not { } google)
            {
                run.Came("blocked", "no_google");
                return await AnswerAsync(request, HttpStatusCode.Conflict, "the Google settings are not set: nothing sent", ct);
            }
            var leads = await new SqlCampaignStore(sql).OpenAsync(ct);
            var mail = leads.Where(l => !string.IsNullOrWhiteSpace(l.ContactEmail)).Select(l => (Lead: l, Mail: TestMail(l, to, time.GetUtcNow()))).FirstOrDefault(x => x.Mail is not null);
            if (mail.Mail is null)
            {
                run.Came("unchanged", "no_lead");
                return await AnswerAsync(request, HttpStatusCode.Conflict, "no open lead with an address renders: nothing sent", ct);
            }
            var sent = await new GmailSender(http, google).SendAsync(mail.Mail, ct);
            string line = $"{CentralClock.Local(time.GetUtcNow()):yyyy-MM-dd HH:mm:ss} test of lead {mail.Lead.Id} ({mail.Lead.CompanyName}, track {mail.Lead.Track}) sent to the test address, gmail {sent.MessageId}";
            await store.EnsureFolderAsync("campaign", ct);
            await store.AppendAsync(Log, TrackerCsvText.Encode(line + "\r\n"), ct);
            run.Count("sent", 1).Came("success", "test");
            return await AnswerAsync(request, HttpStatusCode.OK, line, ct);
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
    /// The lead's email as the send would render it, addressed to the test address only, the subject marked and the
    /// lead's code swapped for the test code; null when the template leaves a blank.
    /// </summary>
    public static OutgoingMail? TestMail(LeadRecord lead, string to, DateTimeOffset sendAtUtc)
    {
        var email = TemplateRender.Render(CampaignTemplate.For(lead.Track), TemplateRender.Values(lead, sendAtUtc), TemplateRender.OptionalFor(lead));
        if (!email.Complete)
        {
            return null;
        }
        string Swap(string text) => lead.RefCode is { Length: > 0 } code ? text.Replace("r=" + code, "r=" + TestCode, StringComparison.Ordinal) : text;
        return new OutgoingMail(to.Trim(), "[TEST] " + email.Subject, Swap(email.Text!), Swap(email.Html!));
    }

    /// <summary>Whether the setting is one plain address, not a list or a display name, so the test goes to one inbox only.</summary>
    public static bool OneAddress(string? value) =>
        value?.Trim() is { Length: > 0 } to && System.Net.Mail.MailAddress.TryCreate(to, out var address) && string.Equals(address.Address, to, StringComparison.Ordinal);

    private static async Task<HttpResponseData> AnswerAsync(HttpRequestData request, HttpStatusCode status, string text, CancellationToken ct)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(text, ct);
        return response;
    }
}
