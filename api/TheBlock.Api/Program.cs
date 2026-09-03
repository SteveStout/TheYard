using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpLogging;
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

#region composition
// The composition root: what is wired, not how each piece works. Every
// registration is a singleton because the dataset is loaded once and shared;
// InventoryService holds it in a Lazy, so a scoped registration would expand
// 100,000 records per request. The source is built by decoration, a synthetic
// scale-up wrapped around the file reader, which is the onion paying for
// itself (ADR: Program.cs, explained).
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
// The other bidders (ADR-027). A singleton like the buyer's own bids, and for
// the same reason: one room, held in memory, for the life of the container.
// The grace period is the one thing about it worth configuring, and the only
// thing that sets it is the browser suite.
builder.Services.AddSingleton(new MarketService(
    builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)));

// Request bodies are snake_case like everything else on this wire.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

#region problem-details
// Every failure answers RFC 9457 ProblemDetails (ADR-023): one shape for a
// rejected query, a rejected bid and an unhandled exception alike, so a caller
// reads one field, `detail`, for the message. The trace identifier ties the
// response to the request's log line.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);

// Every API call is logged as one structured line: method, path, status,
// duration. The JSON console formatter keeps it machine-readable wherever the
// container's output lands.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});
builder.Logging.AddJsonConsole(options => options.IncludeScopes = false);
#endregion problem-details

#region telemetry
// Application Insights (ADR-024). The connection string is an ingestion key,
// so it is never in the repository: the deploy reads it from Azure at roll
// time and passes it to the container as an environment variable. Absent, as
// it is locally and in every test, this block does nothing and the app runs
// exactly as before, which is why no test needs a fake for it.
string? telemetryConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
bool telemetryOn = !string.IsNullOrWhiteSpace(telemetryConnection)
    && !telemetryConnection.StartsWith("__", StringComparison.Ordinal);
if (telemetryOn)
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = telemetryConnection;
    });
}
// The component's app id is public (it is not a key) and is what the Admin
// tab queries with the container's managed identity.
var telemetry = new TelemetryReader(
    builder.Configuration["Azure:AppInsightsAppId"] ?? "6ff89351-7fcc-4a41-8238-db65c5903c36",
    builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
    // Wired only where the connection string is: the app id has a default and
    // is therefore no evidence at all that this build can read anything.
    enabled: telemetryOn);
#endregion telemetry

var app = builder.Build();

// Materialize the inventory now so a bad dataset fails the process at
// startup, visibly, and not as a 500 on the first request.
app.Services.GetRequiredService<InventoryService>().GetAll();

// First in the pipeline, because it can only catch what is registered after
// it: an unhandled exception becomes a 500 ProblemDetails instead of an empty
// body (ADR-023). The request logger sits behind it so a failed request is
// still logged with its real status.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpLogging();

// The dataset is snake_case; keep the wire shape identical to the source file.
var wireFormat = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
#endregion composition

#region inventory-endpoint
// All filters, sorting, and paging are optional GET parameters, applied
// server-side. The default page is the top 100 by auction time (live and
// ending soonest first). Responses are an envelope: { total, vehicles },
// with each vehicle carrying the server-derived auction facts.
// e.g. /api/vehicles?make=Ford&status=live&sort=price-asc&limit=100&offset=100
app.MapGet("/api/vehicles", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    [AsParameters] VehicleQueryParams query) =>
{
    if (!query.TryBuildFilter(out var filter, out var clock, out var sort, out var error))
    {
        // One failure shape for the whole API (ADR-023): the message a person
        // can act on goes in `detail`, never in a key only this endpoint uses.
        return Results.Problem(detail: error, statusCode: 400, title: "The query could not be read");
    }
    // #region overlays
    // The buyer's bids first, the room's second, and the room only wins where
    // it is actually higher (ADR-027). In the other order the buyer would
    // always look like the high bidder, which is the bug this feature exists
    // to make impossible. Both are skipped entirely when nobody has bid, so
    // the common cold request pays for neither.
    // IsEmpty, not Snapshot().Count: a snapshot is a full dictionary copy, and
    // copying both of them on every inventory request to ask whether they are
    // empty is work that grows with the number of bids ever placed.
    Func<Vehicle, Vehicle>? overlay = (bids.IsEmpty, market.IsEmpty) switch
    {
        (true, true) => null,
        (false, true) => bids.Apply,
        (true, false) => market.Apply,
        _ => vehicle => market.Apply(bids.Apply(vehicle)),
    };
    // #endregion overlays
    var result = inventory.Search(filter, clock, sort, query.EffectiveLimit, query.EffectiveOffset, overlay);
    return Results.Json(new
    {
        total = result.Total,
        vehicles = result.Vehicles.Select(v => VehicleWire.ToWire(v, clock, wireFormat)).ToList(),
    }, wireFormat);
});
#endregion inventory-endpoint

// Dropdown values, computed from the full dataset (the page only ever holds a slice).
app.MapGet("/api/facets", (InventoryService inventory) =>
    Results.Json(inventory.Facets(), wireFormat));

app.MapGet("/api/vehicles/{id}", (InventoryService inventory, BidService bids, MarketService market, string id, long? anchor_ms) =>
{
    if (!Clocks.TryResolve(anchor_ms, out var clock, out var error))
    {
        return Results.Problem(detail: error, statusCode: 400, title: "The query could not be read");
    }
    return inventory.GetById(id) is { } vehicle
        ? Results.Json(VehicleWire.ToWire(market.Apply(bids.Apply(vehicle)), clock, wireFormat), wireFormat)
        : Results.NotFound();
});

#region bid-endpoints
// ---------------------------------------------------------------------------
// Bidding, validated server-side by the domain's BidRules. Single anonymous
// buyer; state lives in API memory (isolated demo).
// ---------------------------------------------------------------------------

app.MapPost("/api/vehicles/{id}/bids", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    string id,
    BidRequest request) => HandleBid(inventory, bids, market, id, request.AnchorMs,
        // The room's standing price is what the minimum next bid is measured
        // against (ADR-027). Handing BidRules the dataset's figure instead
        // would let the buyer retake the lead with a bid below the going rate.
        (vehicle, clock) => bids.PlaceBid(market.Apply(vehicle), request.Amount, clock)));

app.MapPost("/api/vehicles/{id}/buy-now", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    string id,
    BuyNowRequest request) => HandleBid(inventory, bids, market, id, request.AnchorMs,
        (vehicle, clock) => bids.BuyNow(market.Apply(vehicle), clock)));

#region market-endpoints
// The buyer's bids, each one answering the question the badge asks: am I still
// winning this? The server owns that answer because it owns both sides of it.
app.MapGet("/api/bids", (BidService bids, MarketService market) =>
    Results.Json(BidViews.For(bids, market), wireFormat));

// One round of bidding by the room, driven by the page rather than a timer
// (ADR-027). The anchor comes from the caller for the same reason every other
// schedule-dependent call carries one: the browser's midnight decides which
// auctions are live, and a room bidding on a different set than the visitor
// can see would be a bug nobody could reproduce.
app.MapPost("/api/market/tick", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    MarketTickRequest request) =>
{
    if (!Clocks.TryResolve(request.AnchorMs, out var clock, out var error))
    {
        return Results.Problem(detail: error, statusCode: 400, title: "The query could not be read");
    }
    var buyerBids = bids.Snapshot();
    // Candidates: everything the buyer is in on, plus a page of live auctions
    // so the grid moves even when the visitor has bid on nothing.
    var contested = buyerBids.Keys
        .Select(inventory.GetById)
        .Where(v => v is not null)
        .Select(v => v!);
    // Take the first forty live auctions rather than searching for them.
    // Search would derive a status for all hundred thousand rows and then sort
    // the forty-odd thousand matches to keep forty of them, every eight
    // seconds, for every open tab. Nothing here needs the soonest-ending ones;
    // it needs forty live ones, and the room shuffles them anyway.
    var live = inventory.GetAll()
        .Where(v => AuctionSchedule.StatusFor(v.Id, clock) == AuctionStatus.Live)
        .Take(40);
    var candidates = contested.Concat(live).DistinctBy(v => v.Id).ToList();
    var raised = market.Tick(candidates, buyerBids, clock);
    return Results.Json(new { raised = raised.Count, bids = BidViews.For(bids, market) }, wireFormat);
});
#endregion market-endpoints

app.MapDelete("/api/bids", (BidService bids, MarketService market) =>
{
    bids.Reset();
    // The room resets with the buyer. Leaving its bids standing would mean the
    // reset button clears your side of an auction and not the other one, which
    // reads as a bug however carefully it is explained.
    market.Reset();
    return Results.NoContent();
});
#endregion bid-endpoints

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

#region diagram-page
// A diagram on its own page (ADR-020): the SVG inlined in a small HTML document,
// so it opens in a new tab, zooms with the browser, and keeps its text
// selectable. The name is looked up in the catalog; nothing else is read.
app.MapGet("/api/docs/diagrams/{name}", (string name) =>
    DocsCatalog.Diagrams.TryGetValue(name, out var diagram)
        ? Results.Content(
            DiagramPage.Render(diagram.Title, File.ReadAllText(Path.Combine(repoRoot, diagram.File)), diagram.File),
            "text/html; charset=utf-8")
        : Results.NotFound());
#endregion diagram-page

app.MapGet("/api/docs/bicep", () =>
    Results.Text("# infra/main.bicep" + "\n\nThe production design as code: App Service, Front Door, and the origin lock, deployable by flipping parameters. Kept deliberately undeployed; the Hosting overview explains that choice.\n\n```bicep\n" + File.ReadAllText(Path.Combine(repoRoot, "infra", "main.bicep")) + "\n```\n", "text/markdown"));

app.MapGet("/api/docs/resume", () =>
    Results.File(resumePath, "application/pdf"));

// ---------------------------------------------------------------------------
// Build provenance - the version and commit this container was built from,
// baked in as environment variables by the Docker build (ADR-005).
// ---------------------------------------------------------------------------

#region version-endpoint
// Read once at startup, not per request: these are baked into the image and
// cannot change while the process lives (ADR-005).
app.MapGet("/api/version", () => Results.Json(new { version = buildVersion, commit = buildCommit }));
#endregion version-endpoint

#region bid-handling
// One local function behind both bid endpoints, answering three questions in
// order: is the clock anchor valid, does the vehicle exist, does the domain
// accept the action. The order matters, because a bad anchor would make the
// domain's answer meaningless. The status codes are the contract the browser
// relies on (ADR-023).
IResult HandleBid(
    InventoryService inventory,
    BidService bids,
    MarketService market,
    string id,
    long? anchorMs,
    Func<Vehicle, AuctionClock, BidOutcome> action)
{
    if (!Clocks.TryResolve(anchorMs, out var clock, out var clockError))
    {
        return Results.Problem(detail: clockError, statusCode: 400, title: "The bid was rejected");
    }
    if (inventory.GetById(id) is not { } vehicle)
    {
        return Results.NotFound();
    }
    var outcome = action(vehicle, clock);
    if (outcome.Kind == BidOutcomeKind.Rejected)
    {
        return Results.Problem(detail: outcome.Reason, statusCode: 400, title: "The bid was rejected");
    }
    return Results.Json(new
    {
        kind = outcome.Kind.ToString().ToLowerInvariant(),
        amount = outcome.Amount,
        // The room's answer rides back with the bid, so the badge is right
        // the moment the response lands rather than at the next tick.
        // TryGetValue, not the indexer: DELETE /api/bids is public and can
        // land between the bid being recorded and this line reading it back.
        bid = BidViews.For(bids, market).TryGetValue(id, out var view) ? view : null,
        vehicle = VehicleWire.ToWire(market.Apply(bids.Apply(vehicle)), clock, wireFormat),
    }, wireFormat);
}
#endregion bid-handling

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

#region error-log
// Middleware, so it sees every response including the ones no endpoint
// returned. It records and rethrows rather than handling: the ProblemDetails
// handler registered earlier owns the response, this only owns the record
// (ADR-010, ADR-023).
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
#endregion error-log

#region health-checks
// Each probe is timed and each answer is a value, never an exception: a
// health endpoint that throws tells an orchestrator nothing. The checks are
// deliberately about the files this app cannot run without.
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

#region probes
// Three endpoints, three audiences. /healthz is the container's HEALTHCHECK
// and answers only "the process is up". /readyz is the deploy's Verify step
// and answers 503 until the files are in place. /api/health is the Admin tab
// and carries the timings. A process can be alive and not yet ready, and the
// orchestrator treats those differently (ADR-010).
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
#endregion probes

app.MapGet("/api/errors", () => Results.Json(errorLog.Snapshot(), wireFormat));

#region client-errors
// Browser errors land where server errors already do (ADR-023): a render
// crash caught by the boundary, or an unhandled rejection, POSTs here and
// shows up on the Admin tab's Recent errors card tagged with the page the
// visitor was on. Status 0 marks the entry as coming from the browser.
app.MapPost("/api/errors/client", (ClientErrorReport report, ILoggerFactory loggers) =>
{
    if (string.IsNullOrWhiteSpace(report.Message))
    {
        return Results.Problem(detail: "A client error report needs a message.", statusCode: 400,
            title: "The error report could not be read");
    }
    // Bounded on the way in: a stack trace from a minified bundle can be long,
    // and the buffer is a demo's memory, not a log store.
    string message = report.Message.Length > 500 ? report.Message[..500] : report.Message;
    string where = string.IsNullOrWhiteSpace(report.Path) ? "(browser)" : report.Path;
    errorLog.Record(where, 0, "browser: " + message);
    // The same report goes to Application Insights as a structured log, so a
    // browser error is searchable beside the server's own (ADR-024). Logging
    // rather than posting from the browser keeps the page free of a second
    // external script and keeps the ingestion key server-side.
    loggers.CreateLogger("TheBlock.Browser").LogError(
        "Browser error on {Path}: {BrowserMessage} {BrowserStack}", where, message, report.Stack ?? "");
    return Results.NoContent();
});
#endregion client-errors

app.MapGet("/api/admin/azure", async () => Results.Json(await azureSelf.GetStateAsync(), wireFormat));

#region telemetry-endpoint
// The last hour as Application Insights has it, for the Admin tab (ADR-024).
// Answers a shape the card can render even when telemetry is off or the query
// fails, because a panel that reports on the system must not be able to break
// the page it reports from.
app.MapGet("/api/admin/telemetry", async () => Results.Json(await telemetry.GetRecentAsync(), wireFormat));
#endregion telemetry-endpoint

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

#region static-files
// Registered last on purpose. Middleware runs in registration order, so the
// cache rules above must already be in place, and the SPA fallback must be
// the last word: it answers app routes with index.html, while an address that
// looks like a file stays a 404 rather than a page dressed as a script.
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
#endregion static-files

app.Run();

#region find-upward
// Started from three different folders (dotnet run, the test host's bin
// directory, /app in the image), so nothing may assume a fixed depth. Walking
// up until the file appears works from all three, and the throw names what
// was missing instead of failing later as a null.
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
#endregion find-upward

#region records-and-test-hook
/// <summary>Bid submission: the amount plus the client's clock anchor.</summary>
public sealed record BidRequest(int Amount, long? AnchorMs);

/// <summary>Buy-now submission: just the client's clock anchor.</summary>
public sealed record BuyNowRequest(long? AnchorMs);

/// <summary>One round of bidding by the simulated room (ADR-027).</summary>
public sealed record MarketTickRequest(long? AnchorMs);

/// <summary>What the browser reports when a render crashes or a promise rejects (ADR-023).</summary>
public sealed record ClientErrorReport(string? Message, string? Stack, string? Path);

// Exposes the entry point to WebApplicationFactory for integration tests.
public partial class Program;
#endregion records-and-test-hook
