using System.Text.Json;

namespace TheBlock.Api;

// The observability types behind the Admin tab (ADR-010), moved out of
// Program.cs verbatim in the staff review (ADR-017) so the host file stays a
// composition root: what is wired, not how each piece works.

/// <summary>
/// One health probe's outcome and how long it took, serialized snake_case for
/// the Admin tab.
///
/// <para><see cref="GatesReadiness"/> is the difference between "this container
/// is unwell" and "this container cannot serve". Readiness decides whether the
/// orchestrator and the deploy will send traffic here; health is the fuller
/// picture the Admin tab shows. A container running on the file-backed fallback
/// is degraded and entirely able to serve, so the database check reports its
/// failure without withholding the container from service
/// (ADR: The relational store).</para>
/// </summary>
public sealed record HealthCheckEntry(
    string Name, string Status, string Detail, long DurationMs, bool GatesReadiness = true);

/// <summary>One recorded server error, newest first in snapshots.</summary>
public sealed record ErrorEntry(DateTimeOffset At, string Path, int Status, string Message);

/// <summary>
/// Fixed-size, thread-safe buffer of recent server errors. In-memory on
/// purpose for this demo: it resets on every roll, and the Admin tab says so.
/// </summary>
public sealed class ErrorRingBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<ErrorEntry> _entries = new();

    public void Record(string path, int status, string message)
    {
        lock (_gate)
        {
            _entries.Enqueue(new ErrorEntry(DateTimeOffset.UtcNow, path, status, message));
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<ErrorEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>
/// The site asking Azure about itself: a management-plane token from the
/// container group's own user-assigned identity, then a read of this group's
/// resource. Degrades to available=false anywhere that identity endpoint
/// does not exist (local dev, tests), and caches success for 60 seconds.
/// </summary>
public sealed class AzureSelf(string clientId, string resourceId)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "...";

    private readonly object _gate = new();
    private object? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<object> GetStateAsync()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(60))
            {
                return _cached;
            }
        }
        try
        {
            using var tokenReq = new HttpRequestMessage(HttpMethod.Get,
                "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01" +
                "&resource=https%3A%2F%2Fmanagement.azure.com%2F&client_id=" + clientId);
            tokenReq.Headers.Add("Metadata", "true");
            using var tokenResp = await Http.SendAsync(tokenReq);
            tokenResp.EnsureSuccessStatusCode();
            using var tokenJson = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
            string token = tokenJson.RootElement.GetProperty("access_token").GetString()!;

            using var armReq = new HttpRequestMessage(HttpMethod.Get,
                "https://management.azure.com" + resourceId + "?api-version=2023-05-01");
            armReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var armResp = await Http.SendAsync(armReq);
            armResp.EnsureSuccessStatusCode();
            using var arm = JsonDocument.Parse(await armResp.Content.ReadAsStringAsync());

            var props = arm.RootElement.GetProperty("properties");
            string groupState = props.TryGetProperty("instanceView", out var iv)
                && iv.TryGetProperty("state", out var st) ? st.GetString() ?? "unknown" : "unknown";
            var containerProps = props.GetProperty("containers")[0].GetProperty("properties");
            string image = containerProps.GetProperty("image").GetString() ?? "unknown";
            int restarts = 0;
            string containerState = "unknown";
            var events = new List<object>();
            if (containerProps.TryGetProperty("instanceView", out var civ))
            {
                restarts = civ.TryGetProperty("restartCount", out var rc) ? rc.GetInt32() : 0;
                if (civ.TryGetProperty("currentState", out var cs))
                {
                    containerState = cs.TryGetProperty("state", out var css) ? css.GetString() ?? "unknown" : "unknown";
                }
                #region azure-events
                // The last three events Azure recorded for the container (pulls, starts,
                // kills), newest first, each message trimmed: enough to read a restart
                // story from the Admin tab without opening the portal (ADR-010, second pass).
                if (civ.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
                {
                    events = evs.EnumerateArray()
                        .Select(e => new
                        {
                            name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            count = e.TryGetProperty("count", out var c) && c.TryGetInt32(out int ci) ? ci : 1,
                            last_at = e.TryGetProperty("lastTimestamp", out var lt) ? lt.GetString() ?? "" : "",
                            message = Trim(e.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "", 140),
                        })
                        .OrderByDescending(e => e.last_at, StringComparer.Ordinal)
                        .Take(3)
                        .Cast<object>()
                        .ToList();
                }
                #endregion azure-events
            }
            var result = new
            {
                available = true,
                group_state = groupState,
                container_state = containerState,
                restart_count = restarts,
                image,
                events,
                fetched_at = DateTimeOffset.UtcNow,
            };
            lock (_gate)
            {
                _cached = result;
                _cachedAt = DateTimeOffset.UtcNow;
            }
            return result;
        }
        catch (Exception ex)
        {
            return new { available = false, reason = ex.GetType().Name };
        }
    }
}
