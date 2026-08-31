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
// Walk up to the repo root rather than assuming a fixed depth — keeps
// `dotnet run`, tests, and published output all working from one line.
string dataPath = FindUpward(contentRoot, Path.Combine("data", "vehicles.json"));
string readmePath = FindUpward(contentRoot, "README.md");
string dataflowPath = FindUpward(contentRoot, Path.Combine("docs", "DATAFLOW.md"));
string projectsPath = FindUpward(contentRoot, Path.Combine("docs", "PROJECTS.md"));
string resumePath = Path.Combine(contentRoot, "wwwroot", "docs", "resume.pdf");
string manifestPath = Path.Combine(contentRoot, "photo-manifest.json");
string imagesRoot = Path.Combine(contentRoot, "wwwroot", "images");

// The 200-record seed dataset is deterministically expanded to TargetCount
// synthetic records (default 100,000) — scale testing without a giant file.
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
// startup, visibly — not as a 500 on the first request.
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
// Bidding — validated server-side by the domain's BidRules. Single anonymous
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
// About documents — the project README and the author's résumé, surfaced in
// the UI's About menu.
// ---------------------------------------------------------------------------

app.MapGet("/api/docs/readme", () =>
    Results.Text(File.ReadAllText(readmePath), "text/markdown"));

app.MapGet("/api/docs/dataflow", () =>
    Results.Text(File.ReadAllText(dataflowPath), "text/markdown"));

app.MapGet("/api/docs/projects", () =>
    Results.Text(File.ReadAllText(projectsPath), "text/markdown"));

app.MapGet("/api/docs/resume", () =>
    Results.File(resumePath, "application/pdf"));

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

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesRoot),
    RequestPath = "/api/images",
    // The photo set is content-stable; let the browser's HTTP cache keep it
    // for a day instead of re-fetching 50 JPEGs per session.
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = "public, max-age=86400",
});

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
