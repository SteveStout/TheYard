using System.Security.Cryptography;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using TheBlock.Api;
using TheBlock.Application;
using TheBlock.Data;
using TheBlock.Domain;
using TheBlock.Infrastructure;

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
#region persistence
// SQLite through EF Core (ADR: The relational store). The connection string is
// configuration, and without one this process gets a scratch file it deletes on
// the way out: that is what every test wants, and it is a better answer for a
// misconfigured deploy than quietly writing somewhere nobody will look.
string? configuredDatabase = builder.Configuration.GetConnectionString("Yard");
// Azure SQL Database, when there is one to talk to (ADR: The SQL Server
// backend). A separate setting rather than a second meaning for the one above,
// so the SQLite path that a developer, a test and a plain `docker run` all use
// is untouched by the existence of a cloud database. The deploy substitutes
// this at roll time and a failed substitution leaves a placeholder, which
// YardConnection.Choose reads as "no SQL Server here" and falls back.
string? configuredSqlServer = builder.Configuration.GetConnectionString("YardSql");
string scratchDatabase = Path.Combine(Path.GetTempPath(), $"theyard-scratch-{Guid.NewGuid():N}.db");
// Pooling off for a scratch database, which is what makes it deletable
// without a process-wide ClearAllPools. That call empties the pool for every
// connection in the process, and a test run holds ten applications at once
// against ten different databases, so one of them tidying up on shutdown was
// pulling connections out from under the others (the staff review, 2026-09-03,
// confirmed by a test that passed alone and failed in the suite).
string databaseConnection = configuredDatabase ?? $"Data Source={scratchDatabase};Pooling=False";
var yard = YardConnection.Choose(configuredSqlServer, databaseConnection);
#endregion persistence

#region migrate-and-seed
// The schema and the contents, before anything is registered, because the
// answer decides what gets registered. Migrate rather than EnsureCreated: the
// schema's history is a set of files in this repository, so a container
// starting against an older database brings it forward instead of finding a
// shape it half recognises. The JSON readers are still where a fresh database
// gets its contents, which keeps `npm run data` the way the dataset is
// regenerated and means the seed cannot drift from the file it came from.
var database = YardDatabase.Prepare(
    yard,
    new JsonFileVehicleSource(dataPath),
    new JsonFilePhotoManifestSource(manifestPath));

if (database.Ready)
{
    // A factory rather than a scoped context: the two sources and the bid store
    // are singletons that each want a context for the length of one operation,
    // and there is no request scope at startup when the catalogue is read.
    builder.Services.AddDbContextFactory<YardDbContext>(options => yard.Configure(options));
    // Identity's stores want a context per request, and the factory hands out
    // contexts rather than registering one. This is the adapter between the two
    // and the only scoped registration in the application.
    builder.Services.AddScoped(services =>
        services.GetRequiredService<IDbContextFactory<YardDbContext>>().CreateDbContext());
    // The same two ports, now answered out of the database. The synthetic
    // scale-up still decorates the vehicle source, and nothing above this line
    // can tell that the catalogue stopped being a file.
    builder.Services.AddSingleton<IVehicleSource>(services =>
        new SyntheticVehicleSource(
            new EfVehicleSource(services.GetRequiredService<IDbContextFactory<YardDbContext>>()),
            targetCount));
    builder.Services.AddSingleton<IPhotoManifestSource>(services =>
        new EfPhotoManifestSource(services.GetRequiredService<IDbContextFactory<YardDbContext>>()));
    builder.Services.AddSingleton<IBidStore>(services =>
        new EfBidStore(services.GetRequiredService<IDbContextFactory<YardDbContext>>()));
}
else
{
    // The store did not come up. These are the adapters that served this site
    // until the database existed, so the inventory, the filters, the photos and
    // the bidding all still work; the only thing lost is that bids stop
    // outliving the process. A site that serves everything except persistence
    // beats a site that serves nothing, and the health check says which one
    // this is (ADR: The relational store).
    builder.Services.AddSingleton<IVehicleSource>(
        new SyntheticVehicleSource(new JsonFileVehicleSource(dataPath), targetCount));
    builder.Services.AddSingleton<IPhotoManifestSource>(new JsonFilePhotoManifestSource(manifestPath));
    builder.Services.AddSingleton<IBidStore>(NullBidStore.Instance);
}
// #region auth
// Accounts (ADR: Accounts and per-user bids). Identity owns the password
// hashing, the normalised lookups and the account tables, which is the part
// worth not writing twice; the session is a JWT this service signs and reads
// itself, carried in a cookie the page cannot touch.
//
// The signing key is configuration. Without one the process invents a random
// key and says so, which means a deploy signs everybody out and no key is ever
// committed. A production deployment reads it from a secret store; that is the
// one line of this that would change.
string? configuredSigningKey = builder.Configuration["Auth:SigningKey"];
string signingKey = configuredSigningKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var tokens = new TokenIssuer(signingKey, TimeSpan.FromDays(7));
builder.Services.AddSingleton(tokens);

if (database.Ready)
{
    builder.Services
        .AddIdentityCore<YardUser>(options =>
        {
            // Long over ornate. A length requirement is the only one of these
            // that measurably helps, and the rest mostly teach people to write
            // the password down (NIST 800-63B says so at more length).
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireDigit = false;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<YardDbContext>();
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = tokens.Validation;
        options.Events = new JwtBearerEvents
        {
            // The token arrives in a cookie rather than an Authorization
            // header, because a page that can read its own token can leak it.
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(TokenIssuer.CookieName, out string? cookie))
                {
                    context.Token = cookie;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();
// #endregion auth

#endregion migrate-and-seed
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
// What goes in that shape when nothing planned the failure: which exceptions
// are the caller's fault and may say so, what a 500 is allowed to reveal, and
// the log line that makes the returned trace id worth having (ADR-030).
builder.Services.AddExceptionHandler<ProblemHandler>();
// A body that will not parse answers a bare 400 with nothing in it by default,
// because the framework would rather not spend an exception on a bad request.
// That left one kind of failure on this API with no sentence in it. One shape
// for every failure is worth an exception on a request that was already wrong
// (ADR-030).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

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

var contexts = app.Services.GetService<IDbContextFactory<YardDbContext>>();

if (database.Ready)
{
    app.Logger.LogInformation("Database ready: {Note}", database.Note);
}
else
{
    // "The store" rather than "the database": Prepare also reads the seed
    // files, so this line covers a missing dataset as well as a database that
    // will not open, and naming only one of them sends the next person to the
    // wrong place (the staff review, 2026-09-03).
    app.Logger.LogError(
        "The store could not be prepared, which covers both the database and the seed files it fills from. The catalogue is being served from the JSON files and bids will not outlive this process: {Note}",
        database.Note);
}

if (configuredDatabase is null && yard.Provider == YardProvider.Sqlite)
{
    app.Logger.LogWarning(
        "No ConnectionStrings:Yard is configured, so this process is using a scratch database at {Path} and will delete it on shutdown",
        scratchDatabase);
    // A scratch database belongs to one process, so it goes when the process
    // does. The pool has to be emptied first or the handle is still open and
    // the delete fails on Windows.
    app.Lifetime.ApplicationStopped.Register(() =>
    {
        // SQLite writes three files, not one: the database, the write-ahead log
        // and the shared-memory index. Deleting only the first leaves the other
        // two behind, and this runs whether or not the store came up, because
        // the half-created file is exactly the case that used to leak (the
        // staff review, 2026-09-03).
        foreach (string leftover in new[]
                 {
                     scratchDatabase,
                     scratchDatabase + "-wal",
                     scratchDatabase + "-shm",
                     scratchDatabase + "-journal",
                 })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover scratch file is litter, not a reason to fail a shutdown.
            }
        }
    });
}

// Materialize the inventory now so a bad dataset fails the process at
// startup, visibly, and not as a 500 on the first request.
app.Services.GetRequiredService<InventoryService>().GetAll();

// First in the pipeline, because it can only catch what is registered after
// it: an unhandled exception becomes a 500 ProblemDetails instead of an empty
// body (ADR-023), filled in by ProblemHandler (ADR-030). The request logger
// sits behind it so a failed request is still logged with its real status.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpLogging();

// Before any endpoint, so a request carries its user by the time one runs. The
// reads do not require it and still get a principal when a cookie is present,
// which is how the listing knows whose badges to draw.
app.UseAuthentication();
app.UseAuthorization();

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
    HttpContext http,
    string id,
    BidRequest request) => HandleBid(inventory, bids, market, http.UserId(), id, request.AnchorMs,
        // The room's standing price is what the minimum next bid is measured
        // against (ADR-027). Handing BidRules the dataset's figure instead
        // would let the buyer retake the lead with a bid below the going rate.
        (vehicle, clock) => bids.PlaceBid(market.Apply(vehicle), request.Amount, clock, http.UserId())))
    .RequireAuthorization();

app.MapPost("/api/vehicles/{id}/buy-now", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    HttpContext http,
    string id,
    BuyNowRequest request) => HandleBid(inventory, bids, market, http.UserId(), id, request.AnchorMs,
        (vehicle, clock) => bids.BuyNow(market.Apply(vehicle), clock, http.UserId())))
    .RequireAuthorization();

#region market-endpoints
// The buyer's bids, each one answering the question the badge asks: am I still
// winning this? The server owns that answer because it owns both sides of it.
// Signed out, this is an empty map rather than a 401: the page asks for it on
// every load, and "you have no bids" is the true answer for somebody who has
// not signed in. The endpoints that change something are the ones that refuse.
app.MapGet("/api/bids", (BidService bids, MarketService market, HttpContext http) =>
    Results.Json(
        http.UserIdOrNull() is { } me
            ? BidViews.For(bids, market, me)
            : new Dictionary<string, BidView>(StringComparer.Ordinal),
        wireFormat));

#region history
// The account page's list, newest first, with the vehicle each bid is on. The
// only query the bids table serves that is not "load everything at startup",
// which is why it is the only reason there is an index on the user column.
app.MapGet("/api/bids/history", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    HttpContext http) =>
{
    var mine = BidViews.For(bids, market, http.UserId());
    var history = mine
        .OrderByDescending(entry => entry.Value.AtMs)
        .Select(entry => new
        {
            vehicle_id = entry.Key,
            title = inventory.GetById(entry.Key) is { } v ? $"{v.Year} {v.Make} {v.Model}" : "(withdrawn)",
            bid = entry.Value,
        })
        .ToList();
    return Results.Json(new { count = history.Count, bids = history }, wireFormat);
}).RequireAuthorization();
#endregion history

// One round of bidding by the room, driven by the page rather than a timer
// (ADR-027). The anchor comes from the caller for the same reason every other
// schedule-dependent call carries one: the browser's midnight decides which
// auctions are live, and a room bidding on a different set than the visitor
// can see would be a bug nobody could reproduce.
app.MapPost("/api/market/tick", (
    InventoryService inventory,
    BidService bids,
    MarketService market,
    HttpContext http,
    MarketTickRequest request) =>
{
    if (!Clocks.TryResolve(request.AnchorMs, out var clock, out var error))
    {
        return Results.Problem(detail: error, statusCode: 400, title: "The query could not be read");
    }
    // Everybody's high-water marks, not one account's. The room answers a
    // price rather than a person, and a room that only responded to whoever
    // happened to be looking would stop being a room the moment there were two
    // of them.
    var buyerBids = bids.StandingAsBids();
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
    return Results.Json(
        new
        {
            raised = raised.Count,
            // The caller's own badges ride back with the tick when there is a
            // caller, so a signed-in page does not need a second request to
            // find out it has been outbid.
            bids = http.UserIdOrNull() is { } me
                ? BidViews.For(bids, market, me)
                : new Dictionary<string, BidView>(StringComparer.Ordinal),
        },
        wireFormat);
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
}).RequireAuthorization();
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
    string userId,
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
        bid = BidViews.For(bids, market, userId).TryGetValue(id, out var view) ? view : null,
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
        Check(
            "database",
            () =>
            {
                if (!database.Ready || contexts is null)
                {
                    return false;
                }
                using var db = contexts.CreateDbContext();
                return db.Vehicles.Any() && db.Photos.Any();
            },
            // The reason is in the log, not in this response. A health endpoint
            // is public on purpose, and an exception message from a storage
            // failure is typically a filesystem path: exactly the map of the
            // inside of the process that ProblemHandler refuses to draw
            // (the staff review, 2026-09-03).
            database.Ready
                ? $"the seed catalogue is in the store ({yard.Describe()})"
                : $"{yard.Describe()} is unavailable, serving the catalogue from files; "
                    + "the reason is in the log"),
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

#region selftest
// A failure on purpose, in production, because every other endpoint here is
// written not to throw and so the exception path had never once run against
// the live container: not the middleware's catch, not the ring buffer's
// record, not the Application Insights exceptions the Admin tab reads. This
// asks all three at once, and the answer it produces is the answer any real
// bug would produce (ADR-030).
app.MapGet("/api/admin/selftest/exception", IResult () =>
    throw new InvalidOperationException(
        "Deliberate self-test failure. No caller ever sees this sentence, which "
        + "is the point of it: it exists to be found in a log and nowhere else."));
#endregion selftest

#region auth-endpoints
// Register, sign in, sign out, and who am I. The token never reaches the page:
// it is set as an httpOnly cookie on the way out and read from the cookie on the
// way back in, so a script on the page cannot read it and cannot be tricked into
// sending it somewhere else (ADR: Accounts and per-user bids).
app.MapPost("/api/auth/register", async (
    IServiceProvider services,
    TokenIssuer issuer,
    HttpContext http,
    Credentials request) =>
{
    if (services.GetService<UserManager<YardUser>>() is not { } users)
    {
        return Accounts.Unavailable();
    }
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Problem(
            detail: "An email address and a password, please.",
            statusCode: 400, title: "The registration could not be read");
    }

    var user = new YardUser
    {
        UserName = request.Email,
        Email = request.Email,
        CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };
    var created = await users.CreateAsync(user, request.Password);
    if (!created.Succeeded)
    {
        return Results.Problem(
            detail: Accounts.Explain(created),
            statusCode: 400, title: "The account was not created");
    }

    http.Response.Cookies.Append(
        TokenIssuer.CookieName,
        issuer.Issue(user.Id, user.Email!),
        TokenIssuer.CookieFor(http, issuer.Lifetime));
    return Results.Json(Accounts.Describe(user), wireFormat);
});

app.MapPost("/api/auth/login", async (
    IServiceProvider services,
    TokenIssuer issuer,
    HttpContext http,
    Credentials request) =>
{
    if (services.GetService<UserManager<YardUser>>() is not { } users)
    {
        return Accounts.Unavailable();
    }

    var user = request.Email is null ? null : await users.FindByEmailAsync(request.Email);
    // One message for "no such account" and for "wrong password", because two
    // messages are an endpoint that tells a stranger which email addresses are
    // registered here.
    if (user is null
        || request.Password is null
        || !await users.CheckPasswordAsync(user, request.Password))
    {
        return Results.Problem(
            detail: "That email address and password do not match an account.",
            statusCode: 401, title: "Not signed in");
    }

    http.Response.Cookies.Append(
        TokenIssuer.CookieName,
        issuer.Issue(user.Id, user.Email!),
        TokenIssuer.CookieFor(http, issuer.Lifetime));
    return Results.Json(Accounts.Describe(user), wireFormat);
});

app.MapPost("/api/auth/logout", (TokenIssuer issuer, HttpContext http) =>
{
    // Deleted with the same attributes it was set with, or the browser keeps a
    // second cookie of the same name on a different path and stays signed in.
    http.Response.Cookies.Delete(TokenIssuer.CookieName, TokenIssuer.CookieFor(http, issuer.Lifetime));
    return Results.Json(Accounts.Anonymous, wireFormat);
});

app.MapGet("/api/auth/me", async (IServiceProvider services, HttpContext http) =>
{
    if (http.UserIdOrNull() is not { } id
        || services.GetService<UserManager<YardUser>>() is not { } users
        || await users.FindByIdAsync(id) is not { } user)
    {
        return Results.Json(Accounts.Anonymous, wireFormat);
    }
    return Results.Json(Accounts.Describe(user), wireFormat);
});
#endregion auth-endpoints

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
