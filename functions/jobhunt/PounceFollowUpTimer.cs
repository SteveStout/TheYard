using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Reads the site's opens every quarter hour for the codes minted on pounce notes, and queues one follow-up item for the
/// dispatcher for each code that reached rung 2 or was opened twice (PounceFollowUps): one per person per role, none for
/// a person who replied, its run line process:PounceFollowUp and one line on nightly/pounce/followups.log for each it
/// found. It sends nothing itself; the item sends only when POUNCE_RUN says live, only on LinkedIn, and only to a leader
/// who accepted the request.
/// </summary>
// #endregion summary
/// <remarks>
/// At 10, 25, 40 and 55 past every hour, five minutes behind the pounce. POUNCE_RUN picks the mode as it does for the
/// hand-off: "off" reads and writes nothing, "live" queues items that may send, anything else, unset included, queues
/// dry items that send nothing. It needs CAMPAIGN_SQL for the tracker's rows; without it the pass is blocked and says
/// so. POST /api/pounce/follow-up/dry-run with the function key runs the dry run, which queues nothing: over the live
/// site, or over two answers opens_report.py saved, named by links= and review= under opens-log/. No model runs here.
/// </remarks>
public sealed class PounceFollowUpTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<PounceFollowUpTimer> log)
{
    private const string Saved = "opens-log/";

    /// <summary>One firing: with the switch not off, one follow-up pass and its run line.</summary>
    [Function("PounceFollowUp")]
    public async Task RunAsync([TimerTrigger("0 10/15 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var run = new RunLine("PounceFollowUp", time.GetUtcNow(), [10, 25, 40, 55]);
        try
        {
            string mode = PounceHandoff.Mode(Environment.GetEnvironmentVariable("POUNCE_RUN"));
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (mode != PounceHandoff.Off && string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                return;
            }
            var pass = mode == PounceHandoff.Off
                ? new FollowUpPass(0, 0, 0, 0, 0, 0, "switch_off")
                : await new PounceFollowUps(store, new SiteVisits(http), new SqlJobsReader(sql!), time).RunAsync(mode, ct);
            Counted(run, pass);
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

    /// <summary>The dry run: the follow-ups a pass would queue and the minutes from each first open, queued nowhere.</summary>
    [Function("PounceFollowUpDryRun")]
    public async Task<HttpResponseData> DryRunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "pounce/follow-up/dry-run")] HttpRequestData request,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        string? links = query["links"], review = query["review"], sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        if (string.IsNullOrWhiteSpace(sql) || (links is not null && !links.StartsWith(Saved, StringComparison.Ordinal)) || (review is not null && !review.StartsWith(Saved, StringComparison.Ordinal)))
        {
            response.StatusCode = HttpStatusCode.BadRequest;
            await response.WriteStringAsync("blocked: CAMPAIGN_SQL is not set, or links= or review= names a file outside opens-log/\n", ct);
            return response;
        }
        ISiteVisits site = links is null ? new SiteVisits(http) : new SavedVisits(await TextAsync(links, ct), review is null ? null : await TextAsync(review, ct));
        var lines = await new PounceFollowUps(store, site, new SqlJobsReader(sql), time).DryRunAsync(ct);
        await response.WriteStringAsync(string.Join("\n", lines) + "\n", ct);
        return response;
    }

    /// <summary>The pass's counts on its run line, one name each; a pass that stopped before judging says why.</summary>
    public static RunLine Counted(RunLine run, FollowUpPass pass)
    {
        if (pass.Blocked is { } why)
        {
            run.Came(why == "switch_off" ? "unchanged" : "blocked", why);
        }
        return run.Count("codes", pass.Codes).Count("opened", pass.Opened).Count("due", pass.Due)
            .Count("queued", pass.Queued).Count("replied", pass.Replied).Count("already", pass.Already);
    }

    private async Task<string> TextAsync(string path, CancellationToken ct) => TrackerCsvText.Decode(await store.ReadAsync(path, ct));

    /// <summary>Two answers opens_report.py saved, read as the site would answer them now.</summary>
    private sealed class SavedVisits(string links, string? review) : ISiteVisits
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<CodeVisit>?> ReadAsync(CancellationToken ct) => Task.FromResult(SiteVisits.Parse(links, review));
    }
}
