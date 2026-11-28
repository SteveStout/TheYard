using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Answers whether the tracker merger is keeping up, in counts and ages only (how many lane files wait, how many
/// past five and ten minutes, how old merge.log is), the switches by name, and whether today is a no-email day with
/// the day of the next campaign send the gate lets through, for the Admin tab's cards; the one file it reads is
/// dispatch/NO-EMAIL-DAYS.txt, with the mail gate's own reader.
/// </summary>
// #endregion summary
/// <remarks>
/// GET /api/merger/health: whether the tracker merger is keeping up, as five numbers (MergerHealth).
/// The Admin tab's nightly card reads it once a minute. It is anonymous because the site holds no
/// key to this app and gets none: the answer is counts and ages only, the same figures the card
/// shows the public, and it lists two folders and reads no file but dispatch/NO-EMAIL-DAYS.txt, whose dates it answers
/// as two fields only (no_email_today, next_email_day) so the campaign card holds on a listed day. It also names the
/// commit the running build was published from, so a deploy can prove the new code is the code answering
/// rather than only that the publish step exited zero, which a warm instance of the old build would also
/// let it believe (ADR-098).
/// </remarks>
public sealed class MergerWatch(IMergeStore store, TimeProvider time, ILogger<MergerWatch> log)
{
    [Function("MergerHealth")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "merger/health")] HttpRequestData request,
        CancellationToken ct)
    {
        var run = new RunLine("MergerHealth", time.GetUtcNow());
        MergerHealth health;
        (bool? Today, string? Next) mail;
        try
        {
            health = await MergerHealth.ReadAsync(store, time.GetUtcNow(), ct);
            mail = await MailDaysAsync(store, time.GetUtcNow(), ct);
            run.Count("waiting", health.Waiting).Count("past_5", health.WaitingOver5).Count("past_10", health.WaitingOver10);
            if (health.LogAgeSeconds is { } age)
            {
                run.Count("newest_log_age_s", age);
            }
        }
        catch (Exception ex) when (Cancelled(ex, ct))
        {
            // The invocation was cancelled before the share answered: the host is draining this instance, or the
            // invocation ran out of time. Nothing was read, so nothing is answered, and the run is blocked rather than
            // failed, because a failed run is what the dead Function alert mails about and nothing here is dead.
            run.Came("blocked", "cancelled");
            return request.CreateResponse(HttpStatusCode.NoContent);
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
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(Answer(health, mail).ToJsonString(), ct);
        return response;
    }

    /// <summary>The route's answer: the merger's counts, the switches, the commit, and the two no-email-day fields, null when the file did not read.</summary>
    /// <param name="health">The merger's counts and ages.</param>
    /// <param name="mail">Whether today is listed and the next send's day, from <see cref="MailDaysAsync"/>.</param>
    public static JsonObject Answer(MergerHealth health, (bool? Today, string? Next) mail)
    {
        var answer = JsonSerializer.SerializeToNode(health)!.AsObject();
        answer["switches"] = JsonSerializer.SerializeToNode(Switches());
        answer["commit"] = Commit;
        answer["no_email_today"] = mail.Today;
        answer["next_email_day"] = mail.Next;
        return answer;
    }

    /// <summary>
    /// The no-email days as the campaign card reads them, from the mail gate's own reader and file
    /// (<see cref="NoEmailDayGate.ReadAsync"/>, dispatch/NO-EMAIL-DAYS.txt): whether today, in Central, is listed, and the
    /// day of the next 8 o'clock send the gate lets through, yyyy-MM-dd: today while its send is still ahead, else the first
    /// day after it the file does not list. Both are null when the file is missing or would not read, so an unread file
    /// never quiets the card's alarm.
    /// </summary>
    /// <param name="share">The job-hunt folder.</param>
    /// <param name="now">The moment, UTC.</param>
    /// <param name="ct">Stops the read.</param>
    public static async Task<(bool? Today, string? Next)> MailDaysAsync(IShareReader share, DateTimeOffset now, CancellationToken ct)
    {
        var days = await NoEmailDayGate.ReadAsync(share, ct);
        if (!days.Read)
        {
            return (null, null);
        }
        var local = CentralClock.Local(now);
        var today = DateOnly.FromDateTime(local);
        var day = local.Hour < CentralClock.CampaignHour ? today : today.AddDays(1);
        for (int i = 0; i < 732 && days.Listed(day); i++)
        {
            day = day.AddDays(1);
        }
        return (days.Listed(today), days.Listed(day) ? null : day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>The commit this build was published from, stamped by <c>dotnet publish -p:SourceRevisionId</c>; null when none was.</summary>
    public static string? Commit { get; } = CommitOf(typeof(MergerWatch).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The commit in an informational version, the part after its plus sign; null when there is none.</summary>
    /// <param name="informational">The assembly's informational version, such as 1.0.0+3f2a.</param>
    public static string? CommitOf(string? informational) =>
        informational?.IndexOf('+', StringComparison.Ordinal) is int plus and >= 0 && plus < informational.Length - 1 ? informational[(plus + 1)..] : null;

    /// <summary>The processes' switches, by name, as the app's settings hold them: names and words only, never another setting.</summary>
    public static readonly IReadOnlyList<string> SwitchNames = ["ATS_SWEEP", "LEADS", "LEADS_LIST", "TRIAGE", "REPLY_ON_HUMAN", "MERGER", "APPENDS", "TRACKER_SQL", "NIGHTLY_ANY_HOUR", "SHEET_MIRROR", "DAILY_CARD", "TODAY_CARD", "FUNNEL", "EMAIL_CAMPAIGN", "NETWORK_LOG", "NETWORK_LOG_ANY_HOUR", "CAMPAIGN_SQL", "FRESH", "POUNCE", "POUNCE_RUN", "WATCH"];

    /// <summary>The switches whose process runs when the setting is not there at all, so unset reads "on" for them.</summary>
    public static readonly IReadOnlyList<string> OnWhenUnset = ["TODAY_CARD", "FUNNEL", "FRESH", "POUNCE"];

    /// <summary>Each switch's value, "off" when it is not set ("on" for the ones in OnWhenUnset); a value that is not one plain word reads "set".</summary>
    public static IReadOnlyDictionary<string, string> Switches() => SwitchNames.ToDictionary(
        name => name,
        name => (Environment.GetEnvironmentVariable(name) ?? (OnWhenUnset.Contains(name) ? "on" : "off")).Trim().ToLowerInvariant() is var v && v.Length is > 0 and <= 12 && v.All(char.IsLetter) ? v : "set",
        StringComparer.Ordinal);

    /// <summary>True when a read stopped because the invocation itself was cancelled, not because the share failed.</summary>
    /// <param name="ex">What the read threw.</param>
    /// <param name="ct">The invocation's cancellation, which the host signals when it drains the instance or times the run out.</param>
    public static bool Cancelled(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;
}
