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
// Walk up to the repo root rather than assuming a fixed depth, which keeps
// `dotnet run`, tests, and published output all working from one line.
string dataPath = FindUpward(contentRoot, Path.Combine("data", "vehicles.json"));
string readmePath = FindUpward(contentRoot, "README.md");
string resumePath = Path.Combine(contentRoot, "wwwroot", "docs", "resume.pdf");
string manifestPath = Path.Combine(contentRoot, "photo-manifest.json");
string imagesRoot = Path.Combine(contentRoot, "wwwroot", "images");
// Live code samples (ADR-014) read whitelisted source files under the repo root,
// which is the folder README.md sits in, both in the image and in a checkout.
string repoRoot = Path.GetDirectoryName(readmePath)!;
// Build provenance (ADR-005), read once: the Docker build bakes both in.
string buildVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
string buildCommit = Environment.GetEnvironmentVariable("APP_COMMIT") ?? "local";

// The 200-record seed dataset is deterministically expanded to TargetCount
// synthetic records (default 100,000): scale testing without a giant file.
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
// startup, visibly, and not as a 500 on the first request.
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
// Bidding, validated server-side by the domain's BidRules. Single anonymous
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
// Documents: every markdown the sidebar can open, served from one endpoint.
// ---------------------------------------------------------------------------

#region docs-endpoint
// One route for every document (ADR-017): the slug is looked up in the catalog
// (DocsCatalog.cs, the same slugs src/components/DocsMenu.tsx carries), the file
// is read from the repo root and its live blocks are expanded (ADR-014). A slug
// missing from the catalog is a 404, never a file read. The Bicep file and the
// resume keep their own routes below because they are not markdown; a literal
// route wins over the {slug} pattern.
app.MapGet("/api/docs/{slug}", (string slug) =>
    DocsCatalog.Files.TryGetValue(slug, out var file)
        ? Results.Text(
            LiveSamples.Expand(File.ReadAllText(Path.Combine(repoRoot, file)), repoRoot, buildCommit),
            "text/markdown")
        : Results.NotFound());
#endregion docs-endpoint

app.MapGet("/api/docs/bicep", () =>
    Results.Text("# infra/main.bicep" + "\n\nThe production design as code: App Service, Front Door, and the origin lock, deployable by flipping parameters. Kept deliberately undeployed; the Hosting overview explains that choice.\n\n```bicep\n" + File.ReadAllText(Path.Combine(repoRoot, "infra", "main.bicep")) + "\n```\n", "text/markdown"));

app.MapGet("/api/docs/resume", () =>
    Results.File(resumePath, "application/pdf"));

// ---------------------------------------------------------------------------
// Build provenance - the version and commit this container was built from,
// baked in as environment variables by the Docker build (ADR-005).
// ---------------------------------------------------------------------------

app.MapGet("/api/version", () => Results.Json(new { version = buildVersion, commit = buildCommit }));

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

#region health-checks
HealthCheckEntry[] RunChecks()
{
    // Each probe is timed: the Admin tab shows the milliseconds beside the check,
    // so a slow disk or a slow lookup shows up before it fails (ADR-010, second pass).
    HealthCheckEntry Check(string name, Func<bool> probe, string detail)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try { return new HealthCheckEntry(name, probe() ? "pass" : "fail", detail, clock.ElapsedMilliseconds); }
        catch (Exception ex) { return new HealthCheckEntry(name, "fail", ex.GetType().Name, clock.ElapsedMilliseconds); }
    }
    return
    [
        Check("dataset file", () => File.Exists(dataPath), "data/vehicles.json present"),
        Check("docs", () => File.Exists(Path.Combine(repoRoot, "docs", "HOSTING.md")), "served documents findable"),
        Check("photo manifest", () => File.Exists(manifestPath), "image manifest present"),
    ];
}
#endregion health-checks

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
        version = buildVersion,
        commit = buildCommit,
        checks,
    }, wireFormat);
});

app.MapGet("/api/errors", () => Results.Json(errorLog.Snapshot(), wireFormat));

app.MapGet("/api/admin/azure", async () => Results.Json(await azureSelf.GetStateAsync(), wireFormat));

#region cache-headers
// Cache rules (ADR-015), from the shape of the address. Vite names every
// bundle file by a hash of its contents, so /assets/* can be kept for a year
// and never goes stale: a new build has new names. Everything that can change
// under the same address (the page, the API, the documents) says no-cache, so
// a browser asks before reusing it. The photo set keeps its own one-day rule
// below, and a response that already chose its rule is left alone.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var response = context.Response;
        if (!response.Headers.ContainsKey("Cache-Control"))
        {
            bool hashedBundleFile = context.Request.Path.StartsWithSegments("/assets")
                && response.StatusCode == StatusCodes.Status200OK
                && !(response.ContentType ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
            response.Headers.CacheControl = hashedBundleFile
                ? "public, max-age=31536000, immutable"
                : "no-cache";
        }
        return Task.CompletedTask;
    });
    await next();
});
#endregion cache-headers

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

// The SPA fallback serves index.html for app routes only; an address that
// looks like a file (a hashed bundle name that no longer exists, say) is a
// 404, never a page dressed as a script.
app.MapFallbackToFile("{*path:nonfile}", "index.html");

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
