using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace JobHunt.Functions;

/// <summary>
/// GET /api/merger/health: whether the tracker merger is keeping up, as five numbers (MergerHealth).
/// The Admin tab's nightly card reads it once a minute. It is anonymous because the site holds no
/// key to this app and gets none: the answer is counts and ages only, the same figures the card
/// shows the public, and it lists two folders and reads nothing inside any file.
/// </summary>
public sealed class MergerWatch(IMergeStore store, TimeProvider time)
{
    [Function("MergerHealth")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "merger/health")] HttpRequestData request,
        CancellationToken ct)
    {
        var health = await MergerHealth.ReadAsync(store, time.GetUtcNow(), ct);
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(JsonSerializer.Serialize(health), ct);
        return response;
    }
}
