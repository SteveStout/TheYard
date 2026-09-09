using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using TheYard.Api;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

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
// Azure Cosmos DB, when there is an account to talk to (ADR: A second store on
// Cosmos DB, and what it costs). A URL and not a credential: the account has no
// keys, and the container authenticates as the managed identity it already
// carries. Not instead of the relational store: beside it. A container with
// both settings runs both stores and a visitor picks one with the toggle at
// the top of the page (ADR: One container, both stores). The same placeholder
// rule as the SQL setting: a failed substitution at roll time reads as "no
// Cosmos DB here", never as an address.
string? configuredCosmos = builder.Configuration["Cosmos:AccountEndpoint"];
bool cosmosConfigured = !string.IsNullOrWhiteSpace(configuredCosmos)
    && !configuredCosmos.StartsWith("__", StringComparison.Ordinal);
// Which store a request gets when it names none: "sql" or "cosmos". The live
// site says sql; the second container says cosmos; a developer or a test
// run that configured the document store and said nothing gets it, which is
// what "the whole suite booted on Cosmos DB" has meant since 1.0.0.89.
string? configuredDefaultStore = builder.Configuration["Store:Default"];
string scratchDatabase = Path.Combine(Path.GetTempPath(), $"theyard-scratch-{Guid.NewGuid():N}.db");
// Pooling off for a scratch database, which is what makes it deletable
// without a process-wide ClearAllPools. That call empties the pool for every
// connection in the process, and a test run holds ten applications at once
// against ten different databases, so one of them tidying up on shutdown was
// pulling connections out from under the others (the staff review, 2026-09-03,
// confirmed by a test that passed alone and failed in the suite).
string databaseConnection = configuredDatabase ?? $"Data Source={scratchDatabase};Pooling=False";
// The relational store is always one of the two: SQL Server when the deploy
// gave one, SQLite otherwise, and "yard" keeps its name because most of this
// file only ever needs the relational side's answer.
var yard = YardConnection.Choose(configuredSqlServer, databaseConnection);
// The document store's operations, for the Admin tab, created before the store
// is so the container checks and the seed are the first lines in it: the cold
// start is the operation the comparison card most wants to show
// (ADR: What the store is actually doing).
var storeLog = new StoreRingBuffer(200);
// The SQL ring, its sibling, created here for the same reason: the seed's
// statements are the first lines in it (ADR: What the database is actually doing).
var sqlLog = new SqlRingBuffer(200);
// The request describer, built by hand rather than resolved, because the
// interceptor that feeds the SQL ring exists before the container does and
// both stores need it. Registered below so the rest of the application shares
// this one instance.
var httpContextAccessor = new HttpContextAccessor();
var currentRequest = new HttpCurrentRequest(httpContextAccessor);
#endregion persistence

#region migrate-and-seed
// The schema and the contents, before anything is registered, because the
// answer decides what gets registered. Migrate rather than EnsureCreated: the
// schema's history is a set of files in this repository, so a container
// starting against an older database brings it forward instead of finding a
// shape it half recognises. The JSON readers are still where a fresh database
// gets its contents, which keeps `npm run data` the way the dataset is
// regenerated and means the seed cannot drift from the file it came from.
//
// Two stores, two of everything below: each store is brought up on its own,
// timed on its own, and stands behind its own catalogue, bids, room and
// accounts (ADR: One container, both stores). What a request gets is decided
// per request, further down; nothing here knows which one a visitor will pick.
var seedVehicles = new JsonFileVehicleSource(dataPath);
var seedPhotos = new JsonFilePhotoManifestSource(manifestPath);
var backendList = new List<Backend>();

// #region sql-backend
// The relational store, on SQL Server or SQLite. A factory rather than a
// scoped context: the two sources and the bid store are singletons that each
// want a context for the length of one operation, and there is no request
// scope at startup when the catalogue is read. The interceptor is what puts
// every statement on the Admin tab, and it is attached here rather than inside
// YardConnection so that the connection type stays a description of where the
// database is.
var sqlStartup = new StartupTimings();
var sqlState = await sqlStartup.Time("prepare", () => YardDatabase.PrepareAsync(yard, seedVehicles, seedPhotos));
ContextFactory? contexts = null;
if (sqlState.Ready)
{
    var sqlOptions = new DbContextOptionsBuilder<YardDbContext>();
    yard.Configure(sqlOptions);
    sqlOptions.AddInterceptors(new SqlLogInterceptor(sqlLog, currentRequest));
    contexts = new ContextFactory(sqlOptions.Options);
}
backendList.Add(new Backend
{
    Key = "sql",
    Name = yard.Describe(),
    Database = sqlState,
    Startup = sqlStartup,
    Contexts = contexts,
    // The same two ports, answered out of the database, or out of the files
    // when the store did not come up. The synthetic scale-up still decorates
    // the vehicle source, and nothing above this line can tell that the
    // catalogue stopped being a file (ADR: The relational store).
    Inventory = contexts is not null
        ? new InventoryService(new SyntheticVehicleSource(new EfVehicleSource(contexts), targetCount), new EfPhotoManifestSource(contexts))
        : new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
    Bids = new BidService(contexts is not null ? new EfBidStore(contexts) : NullBidStore.Instance),
    Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
    Probe = async () =>
    {
        if (contexts is null)
        {
            return false;
        }
        using var db = contexts.CreateDbContext();
        return await db.Vehicles.AnyAsync() && await db.Photos.AnyAsync();
    },
    // Identity's own store over the accounts tables, given a context from the
    // request's scope. The type is the one AddEntityFrameworkStores would have
    // registered for a user type with no roles; naming it here is what lets
    // the other backend register a different one under the same interface.
    UserStore = contexts is null
        ? _ => null
        : services => new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserOnlyStore<YardUser, YardDbContext, string>(
            services.GetRequiredService<YardDbContext>(),
            services.GetService<IdentityErrorDescriber>()),
});
// #endregion sql-backend

// #region cosmos-backend
// The document store, when there is an account to talk to. Same question, same
// answer shape, other store: are the containers there with the keys this code
// was written for, and is the seed in them. A refusal falls through to the
// file-backed catalogue exactly as a missing schema does on SQL Server.
CosmosStore? cosmos = null;
if (cosmosConfigured)
{
    cosmos = CosmosStore.Connect(
        configuredCosmos!,
        builder.Configuration["Cosmos:Database"] ?? "theyard",
        builder.Configuration["Cosmos:ContainerPrefix"] ?? "",
        // "managed-identity" on the deployed container; anything else, which
        // is what a developer's machine and the test runner have, means the
        // signed-in Azure CLI session.
        builder.Configuration["Cosmos:Credential"] ?? "azure-cli",
        builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
        storeLog);
    // Filed under the request that caused it from the first operation on: the
    // describer exists before the store does now, so no operation is
    // attributed to nobody (ADR: What the store is actually doing).
    cosmos.CurrentRequest = currentRequest;
    var cosmosStartup = new StartupTimings();
    var cosmosState = await cosmosStartup.Time("prepare", () => cosmos.PrepareAsync(seedVehicles, seedPhotos));
    var store = cosmos;
    backendList.Add(new Backend
    {
        Key = "cosmos",
        Name = "Azure Cosmos DB",
        Database = cosmosState,
        Startup = cosmosStartup,
        Cosmos = cosmos,
        // The same three ports, answered out of the document store, or out of
        // the files when it did not come up (ADR: A second store on Cosmos DB,
        // and what it costs).
        Inventory = cosmosState.Ready
            ? new InventoryService(new SyntheticVehicleSource(new CosmosVehicleSource(store), targetCount), new CosmosPhotoManifestSource(store))
            : new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
        Bids = new BidService(cosmosState.Ready ? new CosmosBidStore(store) : NullBidStore.Instance),
        Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
        Probe = () => cosmosState.Ready ? store.ProbeAsync() : Task.FromResult(false),
        // One document per account and one per address, and none of Identity's
        // seven tables (ADR: Accounts on a document store).
        UserStore = cosmosState.Ready ? _ => new CosmosUserStore(store) : _ => null,
    });
}
// #endregion cosmos-backend

// Which one a request gets when it names none, and the answer every request
// reads from its own cookie or header (ADR: One container, both stores).
// Peer:Site is the other container as a visitor reaches it, which behind the
// edge is the domain and not the origin the peer endpoint reads (ADR: One
// container, both stores, addendum). Unset everywhere but the deployed groups.
var backends = new Backends(
    backendList,
    configuredDefaultStore ?? (cosmosConfigured ? "cosmos" : "sql"),
    builder.Configuration["Peer:Site"]);
builder.Services.AddSingleton(backends);
builder.Services.AddScoped<CurrentBackend>();
if (contexts is not null)
{
    builder.Services.AddSingleton<IDbContextFactory<YardDbContext>>(contexts);
    // Identity's stores want a context per request, and the factory hands out
    // contexts rather than registering one. This is the adapter between the two
    // and one of the two scoped registrations in the application.
    builder.Services.AddScoped(services =>
        services.GetRequiredService<IDbContextFactory<YardDbContext>>().CreateDbContext());
}
if (cosmos is not null)
{
    builder.Services.AddSingleton(cosmos);
}

#region admin-rings
// The three rings behind the Admin tab's new sections, registered here because
// the SQL one has to exist before the context factory that feeds it. All three
// are this process's memory and nothing else: they empty on every roll, which
// the page says out loud (ADR: What the database is actually doing).
var logLog = new LogRingBuffer(300);
var requestLog = new RequestRingBuffer(500);
builder.Services.AddSingleton(sqlLog);
builder.Services.AddSingleton<ISqlLog>(sqlLog);
builder.Services.AddSingleton(storeLog);
builder.Services.AddSingleton<IStoreLog>(storeLog);
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
builder.Services.AddSingleton<ICurrentRequest>(currentRequest);
builder.Logging.AddProvider(new RingBufferLoggerProvider(logLog));

// The endpoints that exist to be read by the Admin tab, which are excluded from
// the Admin tab's own numbers.
//
// Named one by one rather than matched on the /api/admin/ prefix, because
// /api/admin/selftest/exception is not an observability read: it is the
// deliberate failure that proves the error path works, and it is exactly the
// request the timing section should be showing.
var observabilityReads = new HashSet<string>(StringComparer.Ordinal)
{
    "/api/admin/sql",
    "/api/admin/store",
    "/api/admin/logs",
    "/api/admin/metrics",
    "/api/admin/peer",
    "/api/admin/experiment",
    "/api/admin/proof",
    "/api/admin/azure",
    "/api/admin/telemetry",
    "/api/errors",
    "/api/health",
    "/readyz",
    "/healthz",
};

// One request, timed and filed, unless it is the Admin tab watching itself.
//
// The observability endpoints are excluded because otherwise they take the
// page over. An open Admin tab polls seven endpoints every thirty seconds, and
// /api/health runs two SQL statements each time it is asked; left in, that is
// fourteen requests a minute of /api/admin/* and enough health-check SELECTs to
// push every real statement out of a two-hundred slot ring inside an hour. The
// section would show nothing but itself, which is the observer effect with a
// literal implementation (the staff review, 2026-09-03).
void RecordRequest(HttpContext context, TimeSpan elapsed)
{
    string path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
    if (observabilityReads.Contains(path))
    {
        return;
    }

    requestLog.Record(new RequestEntry(
        DateTimeOffset.UtcNow,
        context.Request.Method,
        // Bounded, because a request line can be eight kilobytes and five
        // hundred of those is four megabytes of ring nobody asked for.
        path.Length > 200 ? path[..200] + "..." : path,
        context.Response.StatusCode,
        (long)elapsed.TotalMilliseconds,
        // Which store served it, read the same way the request itself was
        // routed, so the comparison card can split one ring two ways.
        backends.For(context).Key));
}
#endregion admin-rings

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

// The hour's allowance of new accounts, for the whole site (ADR: The one write
// a stranger can make). Configurable because the tests need to reach it, and
// because a number that cannot be changed without a deploy is a number nobody
// tunes.
builder.Services.AddSingleton(new RegistrationLimit(
    builder.Configuration.GetValue("Accounts:RegistrationsPerHour", RegistrationLimit.DefaultPerHour),
    () => DateTimeOffset.UtcNow));

// Identity is registered whether or not a store came up, because the store
// is now a per-request choice: the same UserManager serves the relational
// accounts on one request and the document accounts on the next, and a
// request on a backend that has no accounts is refused by the endpoint before
// it asks for one (ADR: One container, both stores).
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
        // #region lockout
        // Five wrong passwords buys five minutes off.
        //
        // Without this, and without it there was nothing, POST /api/auth/login
        // is an unmetered password oracle against real accounts: the endpoint
        // is public, there is no throttle in front of it, and every attempt
        // costs an attacker one request. Five and five is the usual shape and
        // the reason it works is arithmetic rather than strength: it turns
        // thousands of guesses a minute into twelve an hour, per account,
        // which is the difference between a wordlist finishing and not.
        //
        // The refusal after a lockout says the same sentence as a wrong
        // password, deliberately. A distinct "this account is locked" is a
        // reply that confirms the address is registered here, and the login
        // endpoint already goes out of its way not to be that
        // (ADR: A password guess should cost something).
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
        // #endregion lockout
    });
// #region user-store-per-request
// The store behind UserManager, chosen by the request: Identity's own tables
// on the relational backend, one document per account on the document one
// (ADR: Accounts on a document store). The second of the two scoped
// registrations in the application, and the reason the first one exists.
builder.Services.AddScoped<IUserStore<YardUser>>(services =>
    services.GetRequiredService<CurrentBackend>().Backend.UserStore(services)
        ?? throw new InvalidOperationException("this request's store keeps no accounts; the endpoint should have refused it first"));
// #endregion user-store-per-request

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
// #region proof-clients
// Where the performance proof sends its requests: this container's own
// address, with cookies handled by hand because the proof holds one session
// per store and a cookie jar would merge them (ADR: Same performance,
// proven). The address is known only once the server is listening, so the
// factory reads it when a run starts; a test replaces this registration with
// a client to its own in-memory server.
string? selfUrl = null;
builder.Services.AddSingleton(new ProofClients(() => new HttpClient(new HttpClientHandler { UseCookies = false })
{
    BaseAddress = new Uri(selfUrl ?? "http://127.0.0.1:8080"),
    Timeout = TimeSpan.FromSeconds(60),
}));
// #endregion proof-clients
// No InventoryService, BidService or MarketService in the container. Each
// backend owns its own three (the room too: one room per store, held in
// memory for the life of the container, ADR-027), and an endpoint reaches
// them through CurrentBackend, which is scoped to the request that chose the
// store. A singleton of any of the three would be a singleton of one store.

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

// Entity Framework's own command log goes where every other log line goes,
// which the Admin tab's log section and a test both rely on.
contexts?.Attach(app.Services.GetRequiredService<ILoggerFactory>());

// The address the proof talks to itself on, read once the server is up.
// Kestrel reports the wildcard it bound ("http://[::]:8080"), which is not
// an address a client can dial, so the host becomes the loopback.
app.Lifetime.ApplicationStarted.Register(() =>
{
    string? bound = app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal)) ?? app.Urls.FirstOrDefault();
    if (bound is not null)
    {
        string dialable = bound.Replace("://+:", "://127.0.0.1:", StringComparison.Ordinal).Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal);
        selfUrl = Uri.TryCreate(dialable, UriKind.Absolute, out var parsed)
            ? new UriBuilder(parsed) { Host = parsed.HostNameType == UriHostNameType.Dns && parsed.Host != "localhost" ? parsed.Host : "127.0.0.1" }.Uri.ToString()
            : null;
    }
});

foreach (var backend in backends.All)
{
    if (backend.Database.Ready)
    {
        app.Logger.LogInformation("Database ready: {Note}", backend.Database.Note);
    }
    else
    {
        // "The store" rather than "the database": Prepare also reads the seed
        // files, so this line covers a missing dataset as well as a database that
        // will not open, and naming only one of them sends the next person to the
        // wrong place (the staff review, 2026-09-03).
        // The exception goes in the exception slot, not into the template. What is
        // in the template reaches the Admin tab's log section, which is public; what
        // is in the exception slot reaches the console and Application Insights,
        // and the Admin tab shows only its type. A SqlException here says the server
        // name, the login name and this container's IP address.
        app.Logger.LogError(
            backend.Database.Failure,
            "The store could not be prepared, which covers both the database and the seed files it fills from. The catalogue is being served from the JSON files and bids will not outlive this process: {Note}",
            backend.Database.Note);
    }
}

if (configuredDatabase is null && yard.Provider == YardProvider.Sqlite)
{
    app.Logger.LogWarning(
        "No ConnectionStrings:Yard is configured, so this process is using a scratch database in the system temp folder and will delete it on shutdown");
    // The path itself at Debug, which the Admin tab's log section does not
    // capture. A temp path names the account the process runs as, and that page
    // is public.
    app.Logger.LogDebug("The scratch database is at {Path}", scratchDatabase);
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

// Materialize the inventory and replay the bids now, so a bad dataset fails
// the process at startup, visibly, and not as a 500 on the first request, and
// so no visitor's request is the one that waits for the store. This is the
// warm-up the ports record leans on: after these two lines every synchronous
// read in the application is reading a task that has already finished
// (ADR: The ports learn to wait).
// The default store first, before anything is served, which is the warm-up
// the ports record leans on. The other store is warmed after the container
// is ready, in the background, one after the other rather than both at once:
// the container has one vCPU, and two expansions of a hundred thousand
// records racing each other would both take longer and neither number would
// be that store's own. Off by default and on in the deploy, because a test
// run boots ten applications at once and ten second expansions nobody asks
// for is memory the machine running the suite does not have to give; a store
// nobody warmed warms itself on its first request, which is the Lazy the
// inventory service has always had (ADR: One container, both stores).
await backends.Default.Startup.Time("catalogue", backends.Default.Inventory.WarmAsync);
await backends.Default.Startup.Time("bids", backends.Default.Bids.LoadAsync);
backends.Default.Startup.Ready();
if (builder.Configuration.GetValue("Store:WarmOthers", false))
{
    _ = Task.Run(async () =>
    {
        foreach (var backend in backends.All.Where(candidate => !ReferenceEquals(candidate, backends.Default)))
        {
            try
            {
                await backend.Startup.Time("catalogue", backend.Inventory.WarmAsync);
                await backend.Startup.Time("bids", backend.Bids.LoadAsync);
                backend.Startup.Ready();
            }
            catch (Exception ex)
            {
                // The type only; the message can carry a host name and this
                // line reaches the public log section.
                app.Logger.LogError("Warming the {Store} store failed with {Exception}; its first visitor will try again", backend.Name, ex.GetType().Name);
            }
        }
    });
}

// First in the pipeline, because it can only catch what is registered after
// it: an unhandled exception becomes a 500 ProblemDetails instead of an empty
// body (ADR-023), filled in by ProblemHandler (ADR-030). The request logger
// sits behind it so a failed request is still logged with its real status.
#region request-timing
// Timing, and it has to be the outermost thing here.
//
// The first version of this sat further down the pipeline, below
// UseExceptionHandler, and its comment claimed it measured "the whole cost a
// caller waited for, including the time spent turning an exception into a
// ProblemDetails". Both halves were false. Unwinding runs inner to outer, so a
// request that threw reached this finally before the handler had written
// anything, and every failed request was recorded as a 200 with the handler's
// time excluded. /api/admin/selftest/exception answers 500 to its caller and
// was appearing in the metrics as 200 (the staff review, 2026-09-03).
//
// Above the handler it sees the status that was actually sent. Above
// UseAuthentication too, so a request rejected with 401 is counted rather than
// short-circuited before it ever reaches the ring.
app.Use(async (context, next) =>
{
    long start = Stopwatch.GetTimestamp();
    try
    {
        await next();
    }
    finally
    {
        RecordRequest(context, Stopwatch.GetElapsedTime(start));
    }
});
#endregion request-timing

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
    CurrentBackend current,
    [AsParameters] VehicleQueryParams query) =>
{
    // The store this request chose, and everything that stands on it
    // (ADR: One container, both stores).
    var (inventory, bids, market) = current;
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
app.MapGet("/api/facets", (CurrentBackend current) =>
    Results.Json(current.Inventory.Facets(), wireFormat));

app.MapGet("/api/vehicles/{id}", (CurrentBackend current, string id, long? anchor_ms) =>
{
    var (inventory, bids, market) = current;
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
// Bidding, validated server-side by the domain's BidRules.
//
// This used to say "single anonymous buyer; state lives in API memory (isolated
// demo)", and every clause of it became false without the sentence changing. A
// bid belongs to an account (ADR: Accounts and per-user bids) and lives in Azure
// SQL Database (ADR: The SQL Server backend), so nothing in this region is
// consequence-free and none of it is only the caller's to change. Two places
// still read as though it were, and both are recorded: ADR: Reset is one
// person's start-over, and ADR: The room needs an account too.
// ---------------------------------------------------------------------------

app.MapPost("/api/vehicles/{id}/bids", (
    CurrentBackend current,
    HttpContext http,
    string id,
    BidRequest request) => HandleBid(current, http.UserId(), id, request.AnchorMs,
        // The room's standing price is what the minimum next bid is measured
        // against (ADR-027). Handing BidRules the dataset's figure instead
        // would let the buyer retake the lead with a bid below the going rate.
        (vehicle, clock) => current.Bids.PlaceBidAsync(current.Market.Apply(vehicle), request.Amount, clock, http.UserId())))
    .RequireAuthorization();

app.MapPost("/api/vehicles/{id}/buy-now", (
    CurrentBackend current,
    HttpContext http,
    string id,
    BuyNowRequest request) => HandleBid(current, http.UserId(), id, request.AnchorMs,
        (vehicle, clock) => current.Bids.BuyNowAsync(current.Market.Apply(vehicle), clock, http.UserId())))
    .RequireAuthorization();

#region market-endpoints
// The buyer's bids, each one answering the question the badge asks: am I still
// winning this? The server owns that answer because it owns both sides of it.
// Signed out, this is an empty map rather than a 401: the page asks for it on
// every load, and "you have no bids" is the true answer for somebody who has
// not signed in. The endpoints that change something are the ones that refuse.
app.MapGet("/api/bids", (CurrentBackend current, HttpContext http) =>
    Results.Json(
        http.UserIdOrNull() is { } me
            ? BidViews.For(current.Bids, current.Market, me)
            : new Dictionary<string, BidView>(StringComparer.Ordinal),
        wireFormat));

#region history
// The account page's list, newest first, with the vehicle each bid is on. The
// only query the bids table serves that is not "load everything at startup",
// which is why it is the only reason there is an index on the user column.
app.MapGet("/api/bids/history", (
    CurrentBackend current,
    HttpContext http) =>
{
    var (inventory, bids, market) = current;
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
    CurrentBackend current,
    HttpContext http,
    MarketTickRequest request) =>
{
    var (inventory, bids, market) = current;
    if (!Clocks.TryResolve(request.AnchorMs, out var clock, out var error))
    {
        return Results.Problem(detail: error, statusCode: 400, title: "The query could not be read");
    }
    // Everybody's high-water marks, not one account's. The room answers a
    // price rather than a person, and a room that only responded to whoever
    // happened to be looking would stop being a room the moment there were two
    // of them.
    //
    // That is still the right rule for what the room bids against. It is not a
    // reason for anybody at all to be allowed to advance it, which is what this
    // endpoint used to permit: no account, no cookie, and a loop of these
    // raises the price on every auction any signed-in visitor is winning, from
    // a stranger with curl, with nothing in the request ring to attribute it to
    // (ADR: The room needs an account too). Signing in is now the price of
    // moving the room, which is the same price as bidding.
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
            // The caller's own badges ride back with the tick, so a page does
            // not need a second request to find out it has been outbid. There
            // is always a caller now: the endpoint requires one.
            bids = http.UserIdOrNull() is { } me
                ? BidViews.For(bids, market, me)
                : new Dictionary<string, BidView>(StringComparer.Ordinal),
        },
        wireFormat);
}).RequireAuthorization();
#endregion market-endpoints

app.MapDelete("/api/bids", async (CurrentBackend current, HttpContext http) =>
{
    var (_, bids, market) = current;
    if (http.UserIdOrNull() is not { } userId)
    {
        return Results.Unauthorized();
    }

    // The caller's bids, and the room's answers on the vehicles the caller
    // touched. The room resets with the buyer, because leaving its bids
    // standing would mean the reset button clears your side of an auction and
    // not the other one. What it no longer does is clear anybody else's: this
    // endpoint used to take no user at all (ADR: Reset is one person's
    // start-over).
    market.Forget(await bids.ResetAsync(userId));
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
async Task<IResult> HandleBid(
    CurrentBackend current,
    string userId,
    string id,
    long? anchorMs,
    Func<Vehicle, AuctionClock, Task<BidOutcome>> action)
{
    var (inventory, bids, market) = current;
    if (!Clocks.TryResolve(anchorMs, out var clock, out var clockError))
    {
        return Results.Problem(detail: clockError, statusCode: 400, title: "The bid was rejected");
    }
    if (inventory.GetById(id) is not { } vehicle)
    {
        return Results.NotFound();
    }
    var outcome = await action(vehicle, clock);
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
        // TryGetValue, not the indexer: a reset can land between the bid being
        // recorded and this line reading it back. That used to be any visitor's
        // reset, because DELETE /api/bids took no user at all; it is now only
        // this account's, from a second tab, which is rarer and just as real.
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
// #region two-rings
// Browser reports get their own fifty slots rather than sharing the server's.
//
// One list was the decision (ADR: Error handling, one shape everywhere)
// and it still is: the Admin tab shows them merged, because one place to look is
// the point. What changed is where they are kept. POST /api/errors/client is
// anonymous by design, and while the message and the stack were bounded, the
// number of reports was not, so fifty posts from anybody evicted every real
// server error from the page an operator would use to diagnose an outage. A
// separate ring means a flood of reports can only push out other reports.
var browserErrors = new ErrorRingBuffer(50);
// #endregion two-rings
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
        // The type, not the message.
        //
        // This buffer is served at /api/errors, unauthenticated, and an
        // exception message is where a framework writes a filesystem path, a
        // connection detail, or the value that broke a constraint. The
        // ProblemDetails handler two regions up already refuses to put one in a
        // response for exactly that reason, and this line was quietly putting
        // the same text on a public page through a different door.
        //
        // The message is not lost. It goes to the console and to Application
        // Insights as a structured exception, where it is behind a sign-in
        // (ADR: Reviewing my own work, which caught the same defect in the log
        // buffer and missed this one).
        errorLog.Record(context.Request.Path, 500, ex.GetType().Name);
        throw;
    }
});
#endregion error-log

#region health-checks
// Each probe is timed and each answer is a value, never an exception: a
// health endpoint that throws tells an orchestrator nothing. The checks are
// deliberately about the files this app cannot run without.
async Task<HealthCheckEntry[]> RunChecksAsync(bool readinessOnly = false)
{
    // Each probe is timed: the Admin tab shows the milliseconds beside the check,
    // so a slow disk or a slow lookup shows up before it fails (ADR-010, second pass).
    // Awaited, because the document store's probe is two point reads over the
    // network and a health check that blocked a thread on them would be the
    // defect the ports record describes (ADR: The ports learn to wait).
    async Task<HealthCheckEntry> Check(string name, Func<Task<bool>> probe, string detail, bool gatesReadiness = true)
    {
        if (readinessOnly && !gatesReadiness)
        {
            // Not asked, so not run. The caller filters these out anyway; this
            // is what stops the probe behind them from happening at all.
            return new HealthCheckEntry(name, "pass", "not asked", 0, gatesReadiness);
        }

        var clock = Stopwatch.StartNew();
        try { return new HealthCheckEntry(name, await probe() ? "pass" : "fail", detail, clock.ElapsedMilliseconds, gatesReadiness); }
        catch (Exception ex) { return new HealthCheckEntry(name, "fail", ex.GetType().Name, clock.ElapsedMilliseconds, gatesReadiness); }
    }
    var checks = new List<HealthCheckEntry>
    {
        await Check("dataset file", () => Task.FromResult(File.Exists(dataPath)), "data/vehicles.json present"),
        await Check("docs", () => Task.FromResult(File.Exists(Path.Combine(repoRoot, "docs", "HOSTING.md"))), "served documents findable"),
        await Check("photo manifest", () => Task.FromResult(File.Exists(manifestPath)), "image manifest present"),
    };
    // One check per store, named by the store, so a container running both
    // says which one is unavailable (ADR: One container, both stores). The
    // first is still called "database" for the deploy's Verify step and the
    // Admin tab's card, which have read that name since ADR-010.
    foreach (var backend in backends.All)
    {
        checks.Add(await Check(
            ReferenceEquals(backend, backends.Default) ? "database" : $"database ({backend.Key})",
            backend.Probe,
            // The reason is in the log, not in this response. A health endpoint
            // is public on purpose, and an exception message from a storage
            // failure is typically a filesystem path: exactly the map of the
            // inside of the process that ProblemHandler refuses to draw
            // (the staff review, 2026-09-03).
            backend.Database.Ready
                ? $"the seed catalogue is in the store ({backend.Name})"
                : $"{backend.Name} is unavailable, serving the catalogue from files; "
                    + "the reason is in the log",
            // The one check that does not gate readiness, which is the whole
            // point of the fallback. A container with no database still serves
            // the catalogue, the filters, the photos and the bidding; the only
            // thing it loses is bids outliving the process. Reporting itself
            // not ready would take a working site out of service, and it did:
            // the 1.0.0.51 deploy failed on `curl -fsS /readyz` while the site
            // it was checking was serving 100,000 vehicles perfectly well.
            gatesReadiness: false));
    }
    return checks.ToArray();
}
#endregion health-checks

#region probes
// Three endpoints, three audiences. /healthz is the container's HEALTHCHECK
// and answers only "the process is up". /readyz is the deploy's Verify step
// and answers 503 until the files are in place. /api/health is the Admin tab
// and carries the timings. A process can be alive and not yet ready, and the
// orchestrator treats those differently (ADR-010).
app.MapGet("/healthz", () => Results.Text("ok"));

// Only the checks that gate it, and only those get run: the database probe is
// two SQL statements whose answer readiness discards, and this endpoint is
// polled by the orchestrator and by every deploy.
app.MapGet("/readyz", async () =>
    (await RunChecksAsync(readinessOnly: true)).Where(check => check.GatesReadiness).All(check => check.Status == "pass")
        ? Results.Text("ready")
        : Results.StatusCode(503));

app.MapGet("/api/health", async () =>
{
    var checks = await RunChecksAsync();
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

// Both rings, merged newest first, so the page is still one list.
app.MapGet("/api/errors", () => Results.Json(
    errorLog.Snapshot()
        .Concat(browserErrors.Snapshot())
        .OrderByDescending(entry => entry.At)
        .ToArray(),
    wireFormat));

#region admin-observability-endpoints
// The raw SQL, newest first. Statement text, parameter names and types, how
// long the database took, and the request that caused it. No parameter values:
// see the comment on SqlStatement for why there is nowhere to put one.
app.MapGet("/api/admin/sql", () => Results.Json(sqlLog.Snapshot(), wireFormat));

// #region store-endpoint
// The document store's operations, newest first: container, kind, the query
// shape, whether it was pinned to one partition or fanned out, and the request
// charge beside the milliseconds. Empty on a relational container, exactly as
// the SQL list is empty on the document one; the page shows whichever the
// container is (ADR: What the store is actually doing).
app.MapGet("/api/admin/store", () => Results.Json(new
{
    store = backends.Named("cosmos")?.Name ?? backends.Default.Name,
    operations = storeLog.Snapshot(),
}, wireFormat));
// #endregion store-endpoint

// The raw log lines, newest first, exactly as the console got them.
app.MapGet("/api/admin/logs", () => Results.Json(logLog.Snapshot(), wireFormat));

// Timing, computed on read from the two rings. The window is whatever the
// rings currently hold, which the page states rather than implying.
app.MapGet("/api/admin/metrics", (HttpContext http) =>
{
    var requests = requestLog.Snapshot();
    var statements = sqlLog.Snapshot();
    long[] requestDurations = requests.Select(entry => entry.DurationMs).ToArray();
    long[] sqlDurations = statements.Select(statement => statement.DurationMs).ToArray();
    // The store this request is on gets the top-level numbers, which is what
    // the comparison card on a single-store container and the peer read have
    // always taken. Every store this container runs is listed below them, each
    // with its own cold start and its own share of the request ring
    // (ADR: One container, both stores).
    var mine = backends.For(http);
    return Results.Json(new
    {
        requests = new
        {
            window = requests.Count,
            p50_ms = Percentiles.Of(requestDurations, 50),
            p95_ms = Percentiles.Of(requestDurations, 95),
            by_path = Percentiles.ByPath(requests),
            // The same window by route, for the comparison card: a bid on one
            // vehicle and a bid on another are one row (ADR: Backends, side by side).
            by_route = Routes.ByRoute(requests),
        },
        // Counts by status, which is the aggregate that makes the timing above
        // mean something: a p95 of eight milliseconds reads very differently
        // when a third of the window is 500s. It also names nobody, which the
        // per-request list it replaced could not say.
        by_status = requests
            .GroupBy(entry => entry.Status)
            .OrderBy(group => group.Key)
            .Select(group => new { status = group.Key, count = group.Count() })
            .ToArray(),
        sql = new
        {
            window = statements.Count,
            p50_ms = Percentiles.Of(sqlDurations, 50),
            p95_ms = Percentiles.Of(sqlDurations, 95),
            max_ms = sqlDurations.Length == 0 ? 0 : sqlDurations.Max(),
        },
        // #region store-metrics
        // The same window over the document store, with what the window cost:
        // total request units, the median charge, the dearest single operation,
        // and how many of them fanned out across partitions. These are the
        // numbers the comparison card puts beside the milliseconds
        // (ADR: Backends, side by side).
        store = StoreMetrics.Of(mine.Name, storeLog.Snapshot()),
        store_by_route = Routes.ChargesByRoute(storeLog.Snapshot()),
        // How this container came up: how long the store took to answer, how
        // long the catalogue and the bids took to load, when it was ready to
        // serve, and what the seed cost. Measured on this container at this
        // start, which is the only honest cold start there is.
        startup = StartupView(mine),
        // #endregion store-metrics
        // #region backends-metrics
        // Every store this container runs, on the same rows the peer answers
        // with, so the card compares two stores in one process the way it
        // compared two containers: the cold start each one had, the requests
        // each one served, and what those cost the one that can say.
        backends = backends.All.Select(backend => new
        {
            key = backend.Key,
            store = backend.Name,
            ready = backend.Ready,
            @default = ReferenceEquals(backend, backends.Default),
            startup = StartupView(backend),
            requests = RequestsView(requests.Where(entry => entry.Store == backend.Key).ToArray()),
            store_metrics = backend.Cosmos is null
                ? null
                : StoreMetrics.Of(backend.Name, storeLog.Snapshot()),
            store_by_route = backend.Cosmos is null
                ? Array.Empty<RouteCharge>()
                : Routes.ChargesByRoute(storeLog.Snapshot()),
            sql = backend.Contexts is null
                ? null
                : new
                {
                    window = statements.Count,
                    p50_ms = Percentiles.Of(sqlDurations, 50),
                    p95_ms = Percentiles.Of(sqlDurations, 95),
                    max_ms = sqlDurations.Length == 0 ? 0 : sqlDurations.Max(),
                },
        }).ToArray(),
        // #endregion backends-metrics
        // No recent_requests list. The first version returned the whole ring,
        // five hundred entries of method, path, status and timing, which is a
        // near-real-time feed of what every other visitor to a public site is
        // doing: which vehicles they opened, which filters they typed. The page
        // never rendered it. Aggregates answer the question the section is for
        // and name nobody (the staff review, 2026-09-03).
    }, wireFormat);

    static object RequestsView(IReadOnlyList<RequestEntry> served)
    {
        long[] durations = served.Select(entry => entry.DurationMs).ToArray();
        return new
        {
            window = served.Count,
            p50_ms = Percentiles.Of(durations, 50),
            p95_ms = Percentiles.Of(durations, 95),
            by_route = Routes.ByRoute(served),
        };
    }

    object StartupView(Backend backend) => new
    {
        store = backend.Name,
        prepare_ms = backend.Startup.Ms("prepare"),
        schema_ms = backend.Database.SchemaMs,
        seed_ms = backend.Database.SeedMs,
        seed_ru = backend.Database.SeedRequestUnits,
        catalogue_ms = backend.Startup.Ms("catalogue"),
        bids_ms = backend.Startup.Ms("bids"),
        ready_ms = backend.Startup.ReadyMs,
        started_at = startedAt,
    };
});

// #region stores-endpoints
// The toggle at the top of the page (ADR: One container, both stores). What
// stores this container runs and which one this request is on; and the switch,
// which is a cookie for a year and nothing else. The page reloads itself
// after switching, because every number it holds was read from the other
// store and a cache of the wrong store's answers is worse than a cold page.
app.MapGet("/api/stores", (HttpContext http) => Results.Json(backends.Describe(http), wireFormat));

app.MapPost("/api/stores/select", (HttpContext http, StoreChoice choice) =>
{
    // A store this container does not have, or one that did not come up, is
    // refused with the sentence the bar shows; the rule is beside the
    // selection rule in Stores.cs so the two cannot drift apart.
    var (chosen, refusal) = backends.Choose(choice.Store);
    if (chosen is null)
    {
        return Results.Problem(detail: refusal, statusCode: 400, title: "That store cannot be chosen");
    }
    http.Response.Cookies.Append(Backends.CookieName, chosen.Key, Backends.CookieFor(http));
    // Describe as the request will read it next time: the cookie is on the
    // response, not the request, so the header path is what says "chosen".
    http.Request.Headers[Backends.HeaderName] = chosen.Key;
    return Results.Json(backends.Describe(http), wireFormat);
});
// #endregion stores-endpoints
#endregion admin-observability-endpoints

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
    // The stack was not bounded here, only the message was. Both come from an
    // unauthenticated POST and both end up on a public page and in Application
    // Insights, so both get a ceiling (the staff review, 2026-09-03).
    string stack = report.Stack is null ? "" : (report.Stack.Length > 2_000 ? report.Stack[..2_000] : report.Stack);
    string where = string.IsNullOrWhiteSpace(report.Path) ? "(browser)" : report.Path;
    browserErrors.Record(where, 0, "browser: " + message);
    // The same report goes to Application Insights as a structured log, so a
    // browser error is searchable beside the server's own (ADR-024). Logging
    // rather than posting from the browser keeps the page free of a second
    // external script and keeps the ingestion key server-side.
    loggers.CreateLogger("TheYard.Browser").LogError(
        "Browser error on {Path}: {BrowserMessage} {BrowserStack}", where, message, stack);
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
    CurrentBackend current,
    TokenIssuer issuer,
    RegistrationLimit limit,
    HttpContext http,
    Credentials request) =>
{
    // The store this request is on keeps no accounts: the relational fallback
    // (ADR: The relational store) or a document store that did not come up.
    // Asked before UserManager is, because UserManager's store is built from
    // this same answer and would throw where this returns a sentence.
    if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
    {
        return Accounts.Unavailable();
    }
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.Problem(
            detail: "An email address and a password, please.",
            statusCode: 400, title: "The registration could not be read");
    }

    // Checked after the request is read and before the password is hashed, so a
    // request that was never going to work does not spend the hour's allowance
    // and a request that is refused does not spend the CPU. The reply says what
    // happened and how long it lasts, and says nothing about how many accounts
    // exist or how much of the allowance is left.
    if (!limit.TryTake())
    {
        return Results.Problem(
            detail: "This demo is not taking new accounts at the moment. Try again in an hour.",
            statusCode: 429, title: "Too many registrations");
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
    CurrentBackend current,
    TokenIssuer issuer,
    HttpContext http,
    Credentials request) =>
{
    if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
    {
        return Accounts.Unavailable();
    }

    var user = request.Email is null ? null : await users.FindByEmailAsync(request.Email);
    // One message for "no such account" and for "wrong password", because two
    // messages are an endpoint that tells a stranger which email addresses are
    // registered here.
    // One sentence for every way this can fail, including a locked account. See
    // the lockout options: a reply that distinguishes them is a reply that tells
    // a stranger which addresses are registered here.
    var refused = Results.Problem(
        detail: "That email address and password do not match an account.",
        statusCode: 401, title: "Not signed in");

    if (user is null || request.Password is null)
    {
        return refused;
    }

    // Asked before the password is checked, so a locked account does not keep
    // answering guesses, and asked through UserManager rather than by reading
    // the column, because that is what knows the window has expired.
    if (await users.IsLockedOutAsync(user))
    {
        return refused;
    }

    if (!await users.CheckPasswordAsync(user, request.Password))
    {
        // The count is the whole mechanism. CheckPasswordAsync on its own does
        // not touch it, which is why this endpoint had a lockout policy on paper
        // and none in practice for as long as it has existed.
        await users.AccessFailedAsync(user);
        return refused;
    }

    // A success clears the count, or five wrong guesses spread over a week would
    // eventually lock somebody out of their own account.
    await users.ResetAccessFailedCountAsync(user);

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

app.MapGet("/api/auth/me", async (IServiceProvider services, CurrentBackend current, HttpContext http) =>
{
    // An account is a row or a document in one store, so a session opened on
    // the other store reads as signed out here, and signs back in when the
    // toggle goes back. The page says so beside the toggle.
    if (http.UserIdOrNull() is not { } id
        || !current.Backend.Ready
        || services.GetService<UserManager<YardUser>>() is not { } users
        || await users.FindByIdAsync(id) is not { } user)
    {
        return Results.Json(Accounts.Anonymous, wireFormat);
    }
    return Results.Json(Accounts.Describe(user), wireFormat);
});
#endregion auth-endpoints

app.MapGet("/api/admin/azure", async () => Results.Json(await azureSelf.GetStateAsync(), wireFormat));

// #region peer-endpoint
// The other container's metrics, read server side with a short patience, so
// the comparison card can put both backends on the same rows whichever tab
// is open. The peer's address is configuration on this container and never
// reaches the browser (ADR: Backends, side by side).
var peer = new PeerReader(
    builder.Configuration["Peer:Url"],
    new HttpClient { Timeout = PeerReader.Patience + TimeSpan.FromSeconds(1) });
app.MapGet("/api/admin/peer", async () => Results.Json(await peer.ReadAsync(), wireFormat));
// #endregion peer-endpoint

// #region proof-endpoints
// The performance proof (ADR: Same performance, proven): read the last result
// or the run in progress, or start one. Starting is public like the rest of
// the Admin tab, and answers 409 while a run is on or for a minute after one,
// which with the two accounts a run registers is what keeps a loop of these
// from spending the hour's registrations on proving the same thing twice.
var proof = new ProofRunner(backends, app.Services.GetRequiredService<ProofClients>(), sqlLog, storeLog);
app.MapGet("/api/admin/proof", () => Results.Json(proof.Status, wireFormat));
app.MapPost("/api/admin/proof", (int? rounds) => proof.TryStart(rounds ?? ProofRunner.DefaultRounds)
    ? Results.Json(new { status = "running" }, wireFormat, statusCode: StatusCodes.Status202Accepted)
    : Results.Problem(
        detail: "A run is in progress, or the last one finished less than a minute ago. The result is on the card.",
        statusCode: StatusCodes.Status409Conflict,
        title: "The proof is busy"));
// #endregion proof-endpoints

// #region experiment-endpoint
// The partition key, live: seven queries against the 100,000-document
// catalogue, with the request charge beside each, run with this container's
// own identity and cached for a minute (ADR: The partition key). On a
// relational container it says so and shows nothing, which is the card's
// fourth empty state.
app.MapGet("/api/admin/experiment", async () => cosmos is null
    ? Results.Json(new { available = false, reason = "this container is not on Azure Cosmos DB", rows = Array.Empty<object>() }, wireFormat)
    : Results.Json(await Experiment.RunAsync(cosmos), wireFormat));
// #endregion experiment-endpoint

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
