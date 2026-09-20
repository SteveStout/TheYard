using System.Text.Json;

namespace TheYard.Api;

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

/// <summary>
/// One recorded server error, newest first in snapshots. <c>Frames</c> is the
/// exception's own stack, trimmed: method names, the file each one is in and
/// the line it is on, which is the source this repository publishes anyway and
/// the fastest way from a 500 on the Admin tab to the line that threw. The
/// exception's message is still not here and still never will be: a message is
/// where a framework writes a connection detail or the value that broke a
/// constraint, and this list is public (ADR: Error handling, the addendum on
/// frames).
/// </summary>
public sealed record ErrorEntry(
    DateTimeOffset At,
    string Path,
    int Status,
    string Message,
    IReadOnlyList<string> Frames);

/// <summary>An exception's stack as the Admin tab shows it: the frames, trimmed to the ones a reader gets through.</summary>
public static class StackFrames
{
    public const int Most = 12;

    public static IReadOnlyList<string> Of(Exception? exception)
    {
        if (exception?.StackTrace is not { Length: > 0 } stack)
        {
            return [];
        }

        return stack
            .Split('\n')
            .Select(line => line.Trim().TrimStart('a', 't', ' ').TrimStart())
            .Where(line => line.Length > 0)
            .Take(Most)
            .ToArray();
    }
}

/// <summary>
/// Fixed-size, thread-safe buffer of recent server errors. In-memory on
/// purpose for this demo: it resets on every roll, and the Admin tab says so.
/// </summary>
public sealed class ErrorRingBuffer(int capacity)
{
    private readonly object _gate = new();
    private readonly Queue<ErrorEntry> _entries = new();

    /// <summary>Where an entry also goes so a roll does not end it (ADR: Logs that outlive the container, the addendum on the cards); unset, nowhere.</summary>
    public Action<ErrorEntry>? Kept { get; set; }

    public void Record(string path, int status, string message, IReadOnlyList<string>? frames = null)
    {
        var entry = new ErrorEntry(DateTimeOffset.UtcNow, path, status, message, frames ?? []);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }

        Kept?.Invoke(entry);
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
/// The site asking Azure about itself: a management-plane token from its own
/// user-assigned identity, then a read of its own resource. Degrades to
/// available=false anywhere that identity does not exist (local dev, tests),
/// and caches success for 60 seconds.
///
/// <para>The resource is whatever <c>Azure:SelfResourceId</c> names, and there
/// have been two kinds. A container group reports a state, a container inside
/// it, a restart count and its recent events. A web app on an App Service plan
/// reports none of the last three: what it has instead is the plan it shares,
/// and that is what the card shows for it, saying so rather than drawing a
/// zero where a restart count used to be (ADR: One plan, two sites).</para>
/// </summary>
public sealed class AzureSelf(string clientId, string resourceId)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "...";

    private readonly object _gate = new();
    private object? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>True when the resource this process was told it is, is a web app rather than a container group.</summary>
    public static bool IsSite(string resourceId) =>
        resourceId.Contains("/providers/Microsoft.Web/sites/", StringComparison.OrdinalIgnoreCase);

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
            string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId);
            object result;
            if (IsSite(resourceId))
            {
                // The site first, because it names the plan; then its
                // configuration and the plan side by side. Either of those two
                // failing costs the card a line, not the card.
                using var site = await ReadAsync(token, resourceId + "?api-version=2023-12-01");
                string? planId = site.RootElement.GetProperty("properties").TryGetProperty("serverFarmId", out var farm) ? farm.GetString() : null;
                var configRead = TryReadAsync(token, resourceId + "/config/web?api-version=2023-12-01");
                var planRead = planId is null ? Task.FromResult<JsonDocument?>(null) : TryReadAsync(token, planId + "?api-version=2023-12-01");
                using var config = await configRead;
                using var plan = await planRead;
                result = ShapeSite(site.RootElement, config?.RootElement, plan?.RootElement, DateTimeOffset.UtcNow);
            }
            else
            {
                using var group = await ReadAsync(token, resourceId + "?api-version=2023-05-01");
                result = ShapeGroup(group.RootElement, DateTimeOffset.UtcNow);
            }
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

    private static async Task<JsonDocument> ReadAsync(string token, string pathAndQuery)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com" + pathAndQuery);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument?> TryReadAsync(string token, string pathAndQuery)
    {
        try
        {
            return await ReadAsync(token, pathAndQuery);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A container group as the card shows it. Public and static so a test can hand it Azure's own JSON.</summary>
    public static object ShapeGroup(JsonElement root, DateTimeOffset fetchedAt)
    {
        var props = root.GetProperty("properties");
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
        return new
        {
            available = true,
            host = "container-instances",
            group_state = groupState,
            container_state = containerState,
            restart_count = restarts,
            image,
            events,
            fetched_at = fetchedAt,
        };
    }

    #region azure-site
    /// <summary>
    /// A web app as the card shows it: its state, the image it was told to run,
    /// and the plan it shares with the other site. App Service keeps no restart
    /// count and no container events where a reader's identity can ask for them,
    /// so neither is here, and the card says that in a sentence rather than
    /// showing a zero nobody measured (ADR: One plan, two sites).
    /// </summary>
    public static object ShapeSite(JsonElement site, JsonElement? config, JsonElement? plan, DateTimeOffset fetchedAt)
    {
        var props = site.GetProperty("properties");
        static string? Text(JsonElement? from, string name) =>
            from is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        JsonElement? configProps = config is { } c && c.TryGetProperty("properties", out var cp) ? cp : null;
        JsonElement? planProps = plan is { } p && p.TryGetProperty("properties", out var pp) ? pp : null;
        JsonElement? planSku = plan is { } q && q.TryGetProperty("sku", out var sku) ? sku : null;

        // "DOCKER|registry/name:tag" is how App Service writes the image it runs.
        string? fx = Text(configProps, "linuxFxVersion");
        string image = fx is null ? "unknown" : fx[(fx.IndexOf('|') + 1)..];

        return new
        {
            available = true,
            host = "app-service",
            group_state = Text(props, "state") ?? "unknown",
            availability = Text(props, "availabilityState") ?? "unknown",
            image,
            always_on = configProps is { } on && on.TryGetProperty("alwaysOn", out var ao) && ao.ValueKind == JsonValueKind.True,
            health_check_path = Text(configProps, "healthCheckPath"),
            plan_name = Text(plan, "name"),
            plan_sku = Text(planSku, "name"),
            plan_sites = planProps is { } counted && counted.TryGetProperty("numberOfSites", out var ns) && ns.TryGetInt32(out int sites) ? sites : (int?)null,
            region = Text(site, "location"),
            events = Array.Empty<object>(),
            fetched_at = fetchedAt,
        };
    }
    #endregion azure-site
}
