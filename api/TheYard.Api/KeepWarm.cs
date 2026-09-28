using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Api;

// #region keep-warm
/// <summary>
/// Keeps every read a visitor can open warm, on both stores, for as long as the
/// container runs (ADR: Kept awake). Measured on 28 September, the first read
/// after a quiet spell was the slow one: 2.0 to 2.2 s for the activity report
/// against about 260 ms warm, 736 ms for the health report against about 190.
/// Every four minutes this sends each public read through the app's own HTTP
/// pipeline, once per store, so the stores' connections, the caches in front of
/// them and the report cache never go cold between visitors.
///
/// <para>In process on purpose: a scheduled workflow starts when a runner is
/// free, not when it was asked, and a loop that lives in the container runs
/// exactly as long as there is something to keep warm. The first pass waits a
/// random part of a minute, so the two containers never fire together; a pass
/// that runs long delays the next rather than overlapping it; and a read that
/// fails is a warning, never an exception, so the next pass always comes.</para>
/// </summary>
public sealed class KeepWarm : BackgroundService
{
    /// <summary>Carried on every read, so the request ring and the kept log leave it out; the agent is what the activity card reads.</summary>
    public const string Header = "X-Yard-Keep-Warm";

    /// <summary>The site's own mark (Hits.IsSelf), so the activity card counts these as the site reading itself and never as people.</summary>
    public const string Agent = "TheYard-SelfRead/1 (keep-warm)";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan MostStagger = TimeSpan.FromSeconds(60);

    private readonly Func<CancellationToken, Task<KeepWarmPass>> _pass;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stagger;
    private readonly ILogger _logger;
    private KeepWarmPass? _last;
    private int _passes;

    public KeepWarm(Func<CancellationToken, Task<KeepWarmPass>> pass, TimeProvider clock, TimeSpan stagger, ILogger logger)
    {
        _pass = pass;
        _clock = clock;
        _stagger = stagger;
        _logger = logger;
    }

    /// <summary>The last pass that finished, for the health report; null until one has.</summary>
    public KeepWarmPass? Last => Volatile.Read(ref _last);

    /// <summary>How many passes have started.</summary>
    public int Passes => Volatile.Read(ref _passes);

    /// <summary>A random wait before the first pass, up to <see cref="MostStagger"/>.</summary>
    public static TimeSpan RandomStagger() => TimeSpan.FromMilliseconds(Random.Shared.Next((int)MostStagger.TotalMilliseconds));

    /// <summary>
    /// The wait before the next pass: what is left of the interval after the one
    /// that just ran, and nothing when it took longer, so a slow pass pushes the
    /// next one back and two passes never run at once.
    /// </summary>
    public static TimeSpan Next(DateTimeOffset started, DateTimeOffset ended)
    {
        TimeSpan left = Interval - (ended - started);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_stagger, _clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTimeOffset started = _clock.GetUtcNow();
                Interlocked.Increment(ref _passes);
                try
                {
                    Volatile.Write(ref _last, await _pass(stoppingToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // The type, never the message: a message can name a server.
                    _logger.LogWarning("A keep-warm pass failed: {Type}", ex.GetType().Name);
                }

                await Task.Delay(Next(started, _clock.GetUtcNow()), _clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The container is stopping; so is the loop.
        }
    }
}

/// <summary>One pass: when it ran, how many reads it sent, how many did not answer 200, and the slowest.</summary>
public sealed record KeepWarmPass(DateTimeOffset At, int Reads, int Failed, long SlowestMs, string? Slowest);

/// <summary>What a pass reads, and the pass itself.</summary>
public static class KeepWarmReads
{
    /// <summary>
    /// Every public read a visitor can open, the Admin tab's cards with the
    /// windows they open on. One vehicle is added per store from the listing
    /// itself, so the vehicle read is a real one.
    /// </summary>
    public static readonly string[] Paths =
    [
        "/api/vehicles",
        "/api/facets",
        "/api/health",
        "/api/stores",
        "/api/version",
        "/api/tests/summary",
        "/api/admin/activity?window=24h",
        "/api/admin/activity?window=7d",
        "/api/admin/activity?window=30d",
        "/api/admin/machines",
        "/api/admin/pages",
        "/api/admin/metrics",
        "/api/admin/sql",
        "/api/admin/store",
        "/api/admin/logs",
        "/api/admin/azure",
        "/api/admin/peer",
        "/api/admin/tests",
        "/api/admin/proof",
        "/api/admin/experiment",
        "/api/admin/telemetry",
        "/api/errors",
    ];

    /// <summary>Every read in <see cref="Paths"/>, once per store, through the client given; a read that fails is counted and the pass goes on.</summary>
    public static async Task<KeepWarmPass> RunAsync(HttpClient self, IReadOnlyList<string> stores, TimeProvider clock, ILogger logger, CancellationToken cancellation)
    {
        DateTimeOffset at = clock.GetUtcNow();
        int reads = 0;
        int failed = 0;
        long slowest = 0;
        string? slowestPath = null;
        foreach (string store in stores)
        {
            foreach (string path in Paths)
            {
                var (status, ms, body) = await ReadAsync(self, path, store, logger, cancellation);
                reads++;
                if (status != 200)
                {
                    failed++;
                }

                if (ms > slowest)
                {
                    slowest = ms;
                    slowestPath = $"{path} ({store})";
                }

                if (path == "/api/vehicles" && FirstVehicle(body) is { } id)
                {
                    var (vehicleStatus, vehicleMs, _) = await ReadAsync(self, $"/api/vehicles/{Uri.EscapeDataString(id)}", store, logger, cancellation);
                    reads++;
                    failed += vehicleStatus == 200 ? 0 : 1;
                    if (vehicleMs > slowest)
                    {
                        slowest = vehicleMs;
                        slowestPath = $"/api/vehicles/{{id}} ({store})";
                    }
                }
            }
        }

        return new KeepWarmPass(at, reads, failed, slowest, slowestPath);
    }

    private static async Task<(int Status, long Ms, string? Body)> ReadAsync(HttpClient client, string path, string store, ILogger logger, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("User-Agent", KeepWarm.Agent);
            request.Headers.TryAddWithoutValidation(KeepWarm.Header, "1");
            request.Headers.TryAddWithoutValidation(Backends.HeaderName, store);
            using var response = await client.SendAsync(request, cancellation);
            string? body = path == "/api/vehicles" ? await response.Content.ReadAsStringAsync(cancellation) : null;
            if (path != "/api/vehicles")
            {
                // Read to the end, so the read is what a visitor's is, then thrown away.
                await response.Content.CopyToAsync(Stream.Null, cancellation);
            }

            if ((int)response.StatusCode != 200)
            {
                logger.LogWarning("Keep-warm read {Path} on {Store} answered {Status}", path, store, (int)response.StatusCode);
            }

            return ((int)response.StatusCode, clock.ElapsedMilliseconds, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            logger.LogWarning("Keep-warm read {Path} on {Store} failed: {Type}", path, store, ex.GetType().Name);
            return (0, clock.ElapsedMilliseconds, null);
        }
    }

    /// <summary>The first vehicle's id in a listing, or null when the listing has none or cannot be read.</summary>
    public static string? FirstVehicle(string? listing)
    {
        if (string.IsNullOrEmpty(listing))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(listing);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            return id.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not a listing this can read; the pass goes on without a vehicle read.
        }

        return null;
    }
}
// #endregion keep-warm
