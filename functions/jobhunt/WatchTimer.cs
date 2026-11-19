using System.Net;
using JobHunt.Application.Features.Watch;
using JobHunt.Domain.Features.Watch;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The watch (the year-end plan's 7a): every 15 minutes at 13, 28, 43 and 58 past, clear of the pounce (:05), the mirror
/// (:07), the follow-up (:10), the sweep (:20, :50) and the top of the hour, one tick reads each test's real source and
/// writes J:\dispatch\WATCH.md. WATCH picks the mode the way SHEET_MIRROR does: shadow writes WATCH.md and heals
/// nothing, on runs the heal ladder, anything else is off. POST /api/watch/hand-run with the function key runs one tick
/// on demand, for the daytime tests, and answers WATCH.md.
/// </summary>
public sealed class WatchTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<WatchTimer> log)
{
    /// <summary>One firing.</summary>
    [Function("Watch")]
    public async Task RunAsync([TimerTrigger("0 13,28,43,58 * * * *")] TimerInfo timer, CancellationToken ct) =>
        await TickAsync(new RunLine("Watch", time.GetUtcNow(), [13, 28, 43, 58]), ct);

    /// <summary>The hand run: one tick, answered as WATCH.md.</summary>
    [Function("WatchHandRun")]
    public async Task<HttpResponseData> HandRunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "watch/hand-run")] HttpRequestData request,
        CancellationToken ct)
    {
        var result = await TickAsync(new RunLine("Watch", time.GetUtcNow()), ct);
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        string text = result.Written ? TrackerCsvText.Decode(await store.ReadAsync(WatchTick.File, ct)) : "WATCH is off: nothing read, nothing written\n";
        await response.WriteStringAsync(text, ct);
        return response;
    }

    /// <summary>One tick under its run line: the counts of pass, fail and not read, and the mode as its word.</summary>
    public async Task<WatchPassResult> TickAsync(RunLine run, CancellationToken ct)
    {
        try
        {
            var mode = WatchTick.Mode(Environment.GetEnvironmentVariable(WatchTick.Setting));
            if (mode == WatchMode.Off)
            {
                run.Came("unchanged", "switch_off");
                return new WatchPassResult([], false);
            }
            var result = await new WatchPass(store, Environment.GetEnvironmentVariable, time, new JobHunt.Infrastructure.Features.Watch.WatchWeb(http)).RunAsync(mode, ct);
            run.Count("pass", result.Results.Count(r => r.Pass == true)).Count("fail", result.Results.Count(r => r.Pass == false))
                .Count("not_read", result.Results.Count(r => r.Pass is null)).Came("success", mode == WatchMode.On ? "on" : "shadow");
            log.LogInformation("watch tick: {Pass} pass, {Fail} fail", run.Counts["pass"], run.Counts["fail"]);
            return result;
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
}
