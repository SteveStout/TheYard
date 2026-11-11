using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Judges every new posting in code within minutes of the fresh pass finding it, and starts the clock on every role
/// that passes (pounce1110 step 2b): one PounceTick a quarter hour, five minutes behind the fresh pass, its row in
/// jobhunt.PounceRoles for each posting judged, its run line process:Pounce, one line on the share's nightly/pounce.log
/// and, when asked, the seven process:PounceDay lines the Admin tab's card reads. It sends nothing and touches no one.
/// </summary>
// #endregion summary
/// <remarks>
/// At 5, 20, 35 and 50 past every hour. POUNCE picks the mode: "off", any case, does nothing, and anything else, unset
/// included, runs, so a redeploy that drops the setting cannot stop it. It needs CAMPAIGN_SQL and jobhunt.PounceRoles;
/// without either the tick is blocked and says so. The day lines are written on the tick at 5 past each hour and on
/// any tick that passed a role or found a touch. POST /api/pounce/hand-run with the function key runs one tick, always
/// with its day lines, and answers its lines. No model runs here.
/// </remarks>
public sealed class PounceTimer(IMergeStore store, TimeProvider time, ILogger<PounceTimer> log)
{
    /// <summary>The pounce's log on the share, one line a tick.</summary>
    public const string Log = "nightly/pounce.log";

    /// <summary>One firing: with the switch not off, one tick and its lines.</summary>
    [Function("Pounce")]
    public async Task RunAsync([TimerTrigger("0 5/15 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        if (FunnelTimer.Mode(Environment.GetEnvironmentVariable("POUNCE")) == "off")
        {
            var run = new RunLine("Pounce", time.GetUtcNow(), [5, 20, 35, 50]).Came("unchanged", "switch_off");
            RunLines.Write(log, run, time.GetUtcNow());
            return;
        }
        var now = time.GetUtcNow();
        await TickAsync(new RunLine("Pounce", now, [5, 20, 35, 50]), CentralClock.Local(now).Minute < 15, "pounce", ct);
    }

    /// <summary>The hand run: one tick with its day lines, answered as its lines.</summary>
    [Function("PounceHandRun")]
    public async Task<HttpResponseData> HandRunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "pounce/hand-run")] HttpRequestData request,
        CancellationToken ct)
    {
        var lines = await TickAsync(new RunLine("Pounce", time.GetUtcNow()), true, "pounce hand", ct);
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(string.Join("\n", lines) + "\n", ct);
        return response;
    }

    /// <summary>
    /// Runs one tick over the database and writes its lines: the log line, the run line and, when the tick wrote them,
    /// the day lines. Answers the log line and the day lines. A tick that throws writes its failed run line and throws.
    /// </summary>
    public async Task<IReadOnlyList<string>> TickAsync(RunLine run, bool days, string kind, CancellationToken ct)
    {
        var lines = new List<string>();
        try
        {
            string? sql = Environment.GetEnvironmentVariable("CAMPAIGN_SQL");
            if (string.IsNullOrWhiteSpace(sql))
            {
                run.Came("blocked", "no_database");
                return ["blocked: CAMPAIGN_SQL is not set"];
            }
            var book = new SqlPounceBook(sql);
            if (!await book.ReadyAsync(ct))
            {
                run.Came("blocked", "no_pounce_table");
                return ["blocked: jobhunt.PounceRoles is not published"];
            }
            var reader = new SqlJobsReader(sql);
            var tick = await new PounceTick(store, reader, reader, book, time).RunAsync(days, ct);
            var ended = time.GetUtcNow();
            run.Came("success", CentralClock.Local(run.Started).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Counted(run, tick);
            string line = LogLine(CentralClock.Local(ended), kind, tick);
            await AppendLogAsync(store, log, run, line, ct);
            lines.Add(line);
            foreach (var day in tick.Days)
            {
                var dayRun = new RunLine("PounceDay", time.GetUtcNow()).Came("success", day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                foreach (var (name, value) in day.Named)
                {
                    dayRun.Count(name, value);
                }
                string text = dayRun.Text(time.GetUtcNow());
                RunLines.Write(log, dayRun, time.GetUtcNow());
                lines.Add(text);
            }
            return lines;
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
    /// Appends the tick's line to the share's log. The log is a report and the tick's rows are in SQL by then, so a share
    /// that does not answer fails nothing: the run line keeps log_failed=1 and the day lines are still written.
    /// </summary>
    public static async Task AppendLogAsync(IMergeStore store, ILogger log, RunLine run, string line, CancellationToken ct)
    {
        try
        {
            await store.EnsureFolderAsync("nightly", ct);
            await store.AppendAsync(Log, TrackerCsvText.Encode(line + "\n"), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "pounce.log not written; the tick's rows are in SQL");
            run.Count("log_failed", 1);
        }
    }

    /// <summary>The tick's counts on its run line, one name each.</summary>
    public static RunLine Counted(RunLine run, PounceTickResult tick) => run
        .Count("fresh", tick.Fresh).Count("passed", tick.Passed.Count).Count("dropped", tick.Dropped)
        .Count("touched", tick.Touched).Count("waiting", tick.Waiting).Count("late", tick.Late);

    /// <summary>
    /// The tick's line on the share, on the Central clock: its counts, then for each role that passed its company, title,
    /// live time and seen time, so a person can read what was caught without opening the table.
    /// </summary>
    public static string LogLine(DateTime local, string kind, PounceTickResult tick)
    {
        string roles = string.Concat(tick.Passed.Select(p =>
            $"; {Flat(p.Role.Company)} | {Flat(p.Title)} | live {CentralClock.Local(p.Role.LiveAt):yyyy-MM-dd HH:mm}{(p.Role.LiveKnown ? "" : " (first seen)")} | seen {CentralClock.Local(p.Role.FirstSeenAt):yyyy-MM-dd HH:mm}"));
        return $"{local:yyyy-MM-dd HH:mm:ss} {kind}: {tick.Fresh} fresh, {tick.Passed.Count} passed, {tick.Dropped} dropped, {tick.Touched} touched, {tick.Waiting} waiting, {tick.Late} late{roles}";
    }

    private static string Flat(string text) => string.Join(' ', text.Split((char[])['\t', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim())).Trim();
}
