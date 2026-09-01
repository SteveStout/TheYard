using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using TheBlock.Api;
using TheBlock.Application;
using TheBlock.Domain;
using TheBlock.Infrastructure;
using TheBlock.Data;

// Inventory + bidding API, composed onion-style: Domain (entities, photo
// selection, auction schedule, filter and bid rules) <- Application
// (InventoryService and BidService use cases) <- Infrastructure (JSON file
// adapters, synthetic scale-up) <- this host. The React app consumes it
// through Vite's /api proxy, so no CORS is needed.

var builder = WebApplication.CreateBuilder(args);

string contentRoot = builder.Environment.ContentRootPath;
// Walk up to the repo root rather than assuming a fixed depth â€” keeps
// `dotnet run`, tests, and published output all working from one line.
string dataPath = FindUpward(contentRoot, Path.Combine("data", "vehicles.json"));
string readmePath = FindUpward(contentRoot, "README.md");
string dataflowPath = FindUpward(contentRoot, Path.Combine("docs", "DATAFLOW.md"));
string projectsPath = FindUpward(contentRoot, Path.Combine("docs", "PROJECTS.md"));
string resumePath = Path.Combine(contentRoot, "wwwroot", "docs", "resume.pdf");
string manifestPath = Path.Combine(contentRoot, "photo-manifest.json");
string imagesRoot = Path.Combine(contentRoot, "wwwroot", "images");

// The 200-record seed dataset is deterministically expanded to TargetCount
// synthetic records (default 100,000) â€” scale testing without a giant file.
int targetCount = builder.Configuration.GetValue("Inventory:TargetCount", 100_000);
builder.Services.AddSingleton<IVehicleSource>(
    new SyntheticVehicleSource(new JsonFileVehicleSource(dataPath), targetCount));
builder.Services.AddSingleton<IPhotoManifestSource>(new JsonFilePhotoManifestSource(manifestPath));
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<BidService>();

// Request bodies are snake_case like everything else on this wire.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();

// Materialize the inventory now so a bad dataset fails the process at
// startup, visibly â€” not as a 500 on the first request.
app.Services.GetRequiredService<InventoryService>().GetAll();

// The dataset is snake_case; keep the wire shape identical to the source file.
var wireFormat = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

// All filters, sorting, and paging are optional GET parameters, applied
// server-side. The default page is the top 100 by auction time (live and
// ending soonest first). Responses are an envelope: { total, vehicles },
// with each vehicle carrying the server-derived auction facts.
// e.g. /api/vehicles?make=Ford&status=live&sort=price-asc&limit=100&offset=100
app.MapGet("/api/vehicles", (
    InventoryService inventory,
    BidService bids,
    [AsParameters] VehicleQueryParams query) =>
{
    if (!query.TryBuildFilter(out var filter, out var clock, out var sort, out var error))
    {
        return Results.BadRequest(new { error });
    }
    var snapshot = bids.Snapshot();
    Func<Vehicle, Vehicle>? overlay = snapshot.Count == 0 ? null : bids.Apply;
    var result = inventory.Search(filter, clock, sort, query.EffectiveLimit, query.EffectiveOffset, overlay);
    return Results.Json(new
    {
        total = result.Total,
        vehicles = result.Vehicles.Select(v => VehicleWire.ToWire(v, clock, wireFormat)).ToList(),
    }, wireFormat);
});

// Dropdown values, computed from the full dataset (the page only ever holds a slice).
app.MapGet("/api/facets", (InventoryService inventory) =>
    Results.Json(inventory.Facets(), wireFormat));

app.MapGet("/api/vehicles/{id}", (InventoryService inventory, BidService bids, string id, long? anchor_ms) =>
{
    if (!Clocks.TryResolve(anchor_ms, out var clock, out var error))
    {
        return Results.BadRequest(new { error });
    }
    return inventory.GetById(id) is { } vehicle
        ? Results.Json(VehicleWire.ToWire(bids.Apply(vehicle), clock, wireFormat), wireFormat)
        : Results.NotFound();
});

// ---------------------------------------------------------------------------
// Bidding â€” validated server-side by the domain's BidRules. Single anonymous
// buyer; state lives in API memory (isolated demo).
// ---------------------------------------------------------------------------

app.MapPost("/api/vehicles/{id}/bids", (
    InventoryService inventory,
    BidService bids,
    string id,
    BidRequest request) => HandleBid(inventory, bids, id, request.AnchorMs,
        (vehicle, clock) => bids.PlaceBid(vehicle, request.Amount, clock)));

app.MapPost("/api/vehicles/{id}/buy-now", (
    InventoryService inventory,
    BidService bids,
    string id,
    BuyNowRequest request) => HandleBid(inventory, bids, id, request.AnchorMs,
        (vehicle, clock) => bids.BuyNow(vehicle, clock)));

app.MapGet("/api/bids", (BidService bids) => Results.Json(bids.Snapshot(), wireFormat));

app.MapDelete("/api/bids", (BidService bids) =>
{
    bids.Reset();
    return Results.NoContent();
});

// ---------------------------------------------------------------------------
// About documents â€” the project README and the author's rÃ©sumÃ©, surfaced in
// the UI's About menu.
// ---------------------------------------------------------------------------

app.MapGet("/api/docs/readme", () =>
    Results.Text(File.ReadAllText(readmePath), "text/markdown"));

app.MapGet("/api/docs/dataflow", () =>
    Results.Text(File.ReadAllText(dataflowPath), "text/markdown"));

app.MapGet("/api/docs/projects", () =>
    Results.Text(File.ReadAllText(projectsPath), "text/markdown"));

app.MapGet("/api/docs/adr-origin", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-001-front-door-origin.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-docker", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-002-docker-packaging.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-naming", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-003-azure-naming.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-pivots", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-004-deployment-pivots.md"))), "text/markdown"));

app.MapGet("/api/docs/hosting", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "HOSTING.md"))), "text/markdown"));

app.MapGet("/api/docs/cicd", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "CICD.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-edge-economics", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-007-edge-economics.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-linux", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-008-linux-containers.md"))), "text/markdown"));

app.MapGet("/api/docs/practices", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "BEST-PRACTICES.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-versioning", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-005-version-footer.md"))), "text/markdown"));

app.MapGet("/api/docs/adr-docs", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-006-docs-and-testing.md"))), "text/markdown"));

app.MapGet("/api/docs/bicep", () =>
    Results.Text("# infra/main.bicep" + "\n\nThe production design as code: App Service, Front Door, and the origin lock, deployable by flipping parameters. Kept deliberately undeployed; the Hosting overview explains that choice.\n\n```bicep\n" + File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("infra", "main.bicep"))) + "\n```\n", "text/markdown"));

app.MapGet("/api/docs/resume", () =>
    Results.File(resumePath, "application/pdf"));

// ---------------------------------------------------------------------------
// Build provenance - the version and commit this container was built from,
// baked in as environment variables by the Docker build (ADR-005).
// ---------------------------------------------------------------------------

app.MapGet("/api/version", () => Results.Json(new
{
    version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev",
    commit = Environment.GetEnvironmentVariable("APP_COMMIT") ?? "local",
}));

IResult HandleBid(
    InventoryService inventory,
    BidService bids,
    string id,
    long? anchorMs,
    Func<Vehicle, AuctionClock, BidOutcome> action)
{
    if (!Clocks.TryResolve(anchorMs, out var clock, out var clockError))
    {
        return Results.BadRequest(new { reason = clockError });
    }
    if (inventory.GetById(id) is not { } vehicle)
    {
        return Results.NotFound();
    }
    var outcome = action(vehicle, clock);
    if (outcome.Kind == BidOutcomeKind.Rejected)
    {
        return Results.BadRequest(new { reason = outcome.Reason });
    }
    return Results.Json(new
    {
        kind = outcome.Kind.ToString().ToLowerInvariant(),
        amount = outcome.Amount,
        bid = bids.Snapshot()[id],
        vehicle = VehicleWire.ToWire(bids.Apply(vehicle), clock, wireFormat),
    }, wireFormat);
}

// ---------------------------------------------------------------------------
// Observability (ADR-010, roughed in 2026-09-01). Three surfaces feed the
// Admin tab: hand-rolled health checks, an in-memory error ring buffer, and
// the container group's own state read from Azure with its managed identity.
// ---------------------------------------------------------------------------

var startedAt = DateTimeOffset.UtcNow;
var errorLog = new ErrorRingBuffer(50);
// Identifiers, not secrets: the identity's client id and this group's ARM path.
var azureSelf = new AzureSelf(
    builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
    builder.Configuration["Azure:SelfResourceId"]
        ?? "/subscriptions/df3b718c-6d99-4904-8102-6f865941f640/resourceGroups/RG-THEYARD-SS/providers/Microsoft.ContainerInstance/containerGroups/aci-theyard-ss");

app.Use(async (context, next) =>
{
    try
    {
        await next();
        if (context.Response.StatusCode >= 500)
        {
            errorLog.Record(context.Request.Path, context.Response.StatusCode, "server error response");
        }
    }
    catch (Exception ex)
    {
        errorLog.Record(context.Request.Path, 500, ex.GetType().Name + ": " + ex.Message);
        throw;
    }
});

HealthCheckEntry[] RunChecks()
{
    HealthCheckEntry Check(string name, Func<bool> probe, string detail)
    {
        try { return new HealthCheckEntry(name, probe() ? "pass" : "fail", detail); }
        catch (Exception ex) { return new HealthCheckEntry(name, "fail", ex.GetType().Name); }
    }
    return
    [
        Check("dataset file", () => File.Exists(dataPath), "data/vehicles.json present"),
        Check("docs", () => File.Exists(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "HOSTING.md"))), "served documents findable"),
        Check("photo manifest", () => File.Exists(manifestPath), "image manifest present"),
    ];
}

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/readyz", () =>
    RunChecks().All(c => c.Status == "pass") ? Results.Text("ready") : Results.StatusCode(503));

app.MapGet("/api/health", () =>
{
    var checks = RunChecks();
    return Results.Json(new
    {
        status = checks.All(c => c.Status == "pass") ? "healthy" : "degraded",
        uptime_seconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
        version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev",
        commit = Environment.GetEnvironmentVariable("APP_COMMIT") ?? "local",
        checks,
    }, wireFormat);
});

app.MapGet("/api/errors", () => Results.Json(errorLog.Snapshot(), wireFormat));

app.MapGet("/api/admin/azure", async () => Results.Json(await azureSelf.GetStateAsync(), wireFormat));

app.MapGet("/api/docs/adr-observability", () =>
    Results.Text(File.ReadAllText(FindUpward(AppContext.BaseDirectory, Path.Combine("docs", "ADR-010-observability.md"))), "text/markdown"));

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesRoot),
    RequestPath = "/api/images",
    // The photo set is content-stable; let the browser's HTTP cache keep it
    // for a day instead of re-fetching 50 JPEGs per session.
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = "public, max-age=86400",
});

app.MapFallbackToFile("index.html");

app.Run();

static string FindUpward(string startDirectory, string relativePath)
{
    for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }
    throw new FileNotFoundException($"Could not locate {relativePath} in or above {startDirectory}");
}

/// <summary>Bid submission: the amount plus the client's clock anchor.</summary>
public sealed record BidRequest(int Amount, long? AnchorMs);

/// <summary>Buy-now submission: just the client's clock anchor.</summary>
public sealed record BuyNowRequest(long? AnchorMs);

// Exposes the entry point to WebApplicationFactory for integration tests.
public partial class Program;

/// <summary>One health probe's outcome, serialized snake_case for the Admin tab.</summary>
public sealed record HealthCheckEntry(string Name, string Status, string Detail);

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
            if (containerProps.TryGetProperty("instanceView", out var civ))
            {
                restarts = civ.TryGetProperty("restartCount", out var rc) ? rc.GetInt32() : 0;
                if (civ.TryGetProperty("currentState", out var cs))
                {
                    containerState = cs.TryGetProperty("state", out var css) ? css.GetString() ?? "unknown" : "unknown";
                }
            }
            var result = new
            {
                available = true,
                group_state = groupState,
                container_state = containerState,
                restart_count = restarts,
                image,
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
