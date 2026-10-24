using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// Answers whether the tracker merger is keeping up, in counts and ages only (how many lane files wait, how many
/// past five and ten minutes, how old merge.log is) and the switches by name, for the Admin tab's card; it reads no
/// file's contents.
/// </summary>
/// <remarks>
/// GET /api/merger/health: whether the tracker merger is keeping up, as five numbers (MergerHealth).
/// The Admin tab's nightly card reads it once a minute. It is anonymous because the site holds no
/// key to this app and gets none: the answer is counts and ages only, the same figures the card
/// shows the public, and it lists two folders and reads nothing inside any file.
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
        try
        {
            health = await MergerHealth.ReadAsync(store, time.GetUtcNow(), ct);
            run.Count("waiting", health.Waiting).Count("past_5", health.WaitingOver5).Count("past_10", health.WaitingOver10);
            if (health.LogAgeSeconds is { } age)
            {
                run.Count("newest_log_age_s", age);
            }
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
        var answer = JsonSerializer.SerializeToNode(health)!.AsObject();
        answer["switches"] = JsonSerializer.SerializeToNode(Switches());
        await response.WriteStringAsync(answer.ToJsonString(), ct);
        return response;
    }

    /// <summary>The processes' switches, by name, as the app's settings hold them: names and words only, never another setting.</summary>
    public static readonly IReadOnlyList<string> SwitchNames = ["ATS_SWEEP", "LEADS", "LEADS_LIST", "TRIAGE", "REPLY_ON_HUMAN", "MERGER", "APPENDS", "NIGHTLY_ANY_HOUR", "SHEET_MIRROR", "DAILY_CARD"];

    /// <summary>Each switch's value, "off" when it is not set; a value that is not one plain word reads "set".</summary>
    public static IReadOnlyDictionary<string, string> Switches() => SwitchNames.ToDictionary(
        name => name,
        name => (Environment.GetEnvironmentVariable(name) ?? "off").Trim().ToLowerInvariant() is var v && v.Length is > 0 and <= 12 && v.All(char.IsLetter) ? v : "set",
        StringComparer.Ordinal);
}
