using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Writes the job hunt's funnel line once a Central day (PLAN-AUTOHUNT rung 6): yesterday's postings scanned, READY,
/// applied, humans reached, human replies and calls booked, as the process:Funnel run line the Admin tab's funnel card
/// reads from Application Insights, and the same line on the share's nightly/funnel.log, which the TODAY card sums
/// into the week's line. No model runs here.
/// </summary>
// #endregion summary
/// <remarks>
/// It has no timer of its own: the morning card's tick (TodayCardTimer, at 30 past every hour) calls
/// <see cref="DailyAsync"/> first, which acts only in the 7 o'clock hour Central, once a day: a day the log already
/// holds a timer line for is left as it was. FUNNEL picks the mode: unset or "on" writes, "off" writes nothing.
/// POST /api/funnel/hand-run?day=yyyy-MM-dd with the function key writes the line for the day named (yesterday when
/// none is) and puts the week's line into TODAY.md in place, mailing nothing.
/// </remarks>
public sealed class FunnelTimer(IMergeStore store, TimeProvider time, ILogger<FunnelTimer> log)
{
    /// <summary>
    /// The once-a-day write the morning card's tick asks for: in the 7 o'clock hour Central, with the switch not off and
    /// no timer line for yesterday yet, writes yesterday's line. A failure is logged in its own run line and never
    /// reaches the card. True when a line was written.
    /// </summary>
    public static async Task<bool> DailyAsync(IMergeStore store, TimeProvider time, ILogger log, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var day = DateOnly.FromDateTime(CentralClock.Local(now)).AddDays(-1);
        if (Mode(Environment.GetEnvironmentVariable("FUNNEL")) == "off" || !CentralClock.IsTodayHour(now))
        {
            return false;
        }
        try
        {
            if (await WrittenAsync(store, day, ct))
            {
                return false;
            }
            await WriteAsync(store, time, log, day, "timer", ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "funnel line not written; the morning card goes on without it");
            return false;
        }
    }

    /// <summary>The hand run: the line for the day asked (yesterday by default), and the week's line put into TODAY.md.</summary>
    [Function("FunnelHandRun")]
    public async Task<HttpResponseData> HandRunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "funnel/hand-run")] HttpRequestData request,
        CancellationToken ct)
    {
        string? asked = System.Web.HttpUtility.ParseQueryString(request.Url.Query)["day"];
        var day = asked is null
            ? DateOnly.FromDateTime(CentralClock.Local(time.GetUtcNow())).AddDays(-1)
            : DateOnly.TryParseExact(asked, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var named) ? named : (DateOnly?)null;
        var response = request.CreateResponse(day is null ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        if (day is null)
        {
            await response.WriteStringAsync("day must read yyyy-MM-dd", ct);
            return response;
        }
        string line = await WriteAsync(store, time, log, day.Value, "hand", ct);
        string week = await SpliceAsync(store, day.Value, ct);
        await response.WriteStringAsync(line + "\n" + week + "\n", ct);
        return response;
    }

    /// <summary>
    /// Counts the day, appends its log line to nightly/funnel.log and writes the run line; the log line is the answer.
    /// A run that throws writes its failed run line and throws.
    /// </summary>
    public static async Task<string> WriteAsync(IMergeStore store, TimeProvider time, ILogger log, DateOnly day, string kind, CancellationToken ct)
    {
        var run = new RunLine(FunnelCount.Process, time.GetUtcNow());
        try
        {
            var counts = FunnelCount.Of(day, await new FunnelSource(store).ReadAsync(day, ct));
            run.Came("success", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            foreach (var (name, value) in counts.Named)
            {
                run.Count(name, value);
            }
            var ended = time.GetUtcNow();
            string line = FunnelWeek.LogLine(CentralClock.Local(ended), kind, run.Text(ended));
            await store.EnsureFolderAsync("nightly", ct);
            await store.AppendAsync(FunnelWeek.Log, TrackerCsvText.Encode(line + "\n"), ct);
            return line;
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

    /// <summary>Puts the week's line, through the day given, into TODAY.md in place, and answers it.</summary>
    public static async Task<string> SpliceAsync(IMergeStore store, DateOnly through, CancellationToken ct)
    {
        string week = FunnelWeek.Text(await new FunnelSource(store).LogAsync(ct), through);
        string card = await store.ExistsAsync(TodayCard.Path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(TodayCard.Path, ct)) : string.Empty;
        await store.WriteAsync(MergePaths.Temp(TodayCard.Path), TrackerCsvText.Encode(FunnelWeek.Splice(card, week)), ct);
        await store.ReplaceAsync(MergePaths.Temp(TodayCard.Path), TodayCard.Path, ct);
        return week;
    }

    /// <summary>Whether the log already holds a timer line for the day.</summary>
    public static async Task<bool> WrittenAsync(IShareReader store, DateOnly day, CancellationToken ct)
    {
        if (!await store.ExistsAsync(FunnelWeek.Log, ct))
        {
            return false;
        }
        string detail = " detail=" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " ";
        return TodayParts.Lines(TrackerCsvText.Decode(await store.ReadAsync(FunnelWeek.Log, ct)))
            .Any(l => l.Contains(" timer: ", StringComparison.Ordinal) && l.Contains(detail, StringComparison.Ordinal));
    }

    /// <summary>The switch's value: "off", any case, and everything else, unset included, "on".</summary>
    public static string Mode(string? value) => string.Equals((value ?? string.Empty).Trim(), "off", StringComparison.OrdinalIgnoreCase) ? "off" : "on";
}
