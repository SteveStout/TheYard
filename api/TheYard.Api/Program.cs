using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using TheYard.Api;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

// Inventory + bidding API, composed onion-style: Domain (entities, photo
// selection, auction schedule, filter and bid rules) <- Application
// (InventoryService and BidService use cases) <- Infrastructure (JSON file
// adapters, synthetic scale-up) <- this host. The React app consumes it
// through Vite's /api proxy, so no CORS is needed.

#region composition
// The composition root: what is wired, not how each piece works. Nearly every
// registration is a singleton because the dataset is loaded once and shared;
// InventoryService holds it in a Lazy, so a scoped registration would expand
// 100,000 records per request. Two are scoped, the request's store and the
// user store over it, because they are decided per request. The source is
// built by decoration, a scale-up around the reader (ADR: Program.cs, explained).
var builder = WebApplication.CreateBuilder(args);

// Walk up to the repo root rather than assuming a fixed depth, which keeps
// `dotnet run`, tests, and published output all working from one line
// (Composition/HostPaths.cs says what each path is).
var paths = HostPaths.Find(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(paths);
string dataPath = paths.DataPath;
string testResultsPath = paths.TestResultsPath;
string manifestPath = paths.ManifestPath;
string imagesRoot = paths.ImagesRoot;
string repoRoot = paths.RepoRoot;
// Build provenance (ADR-005), read once: the Docker build bakes both in.

string buildVersion = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev";
string buildCommit = Environment.GetEnvironmentVariable("APP_COMMIT") ?? "local";
builder.Services.AddSingleton(new BuildInfo(buildVersion, buildCommit));

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
var sqlState = await sqlStartup.Time("prepare", () => StorePrepare.WithTriesAsync(() => YardDatabase.PrepareAsync(yard, seedVehicles, seedPhotos)));
// The backend stands on the files until its store is attached: the same two
// ports answered out of the JSON, the null bid store, no accounts. The
// synthetic scale-up still decorates the vehicle source, and nothing above
// this line can tell that the catalogue stopped being a file (ADR: The
// relational store). Attached at once below when the store came up, or by the
// second chance when it comes up later (ADR: The relational store, the
// addendum on the second chance).
var sqlBackend = new Backend
{
    Key = "sql",
    Name = yard.Describe(),
    Database = sqlState,
    Startup = sqlStartup,
    Inventory = new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
    Bids = new BidService(NullBidStore.Instance),
    Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
    Probe = () => Task.FromResult(false),
    UserStore = _ => null,
};
// Everything that stands on the relational store, built from a state that
// says it came up. The loggers are attached the moment the host exists (the
// factory is read at call time, so an attach after startup gets them too).
ILoggerFactory? hostLoggers = null;
StoreAttachment SqlAttachment(DatabaseState state)
{
    var sqlOptions = new DbContextOptionsBuilder<YardDbContext>();
    yard.Configure(sqlOptions);
    sqlOptions.AddInterceptors(new SqlLogInterceptor(sqlLog, currentRequest));
    var contexts = new ContextFactory(sqlOptions.Options);
    // The same database without the interceptor, for the activity counters:
    // a batch every five seconds would fill the SQL log with the feature that
    // reads the SQL log, the observer effect the request ring designed out
    // (ADR: Site activity, and the line an address does not cross).
    var quietOptions = new DbContextOptionsBuilder<YardDbContext>();
    yard.Configure(quietOptions);
    var quietContexts = new ContextFactory(quietOptions.Options);
    if (hostLoggers is not null)
    {
        contexts.Attach(hostLoggers);
    }
    return new StoreAttachment(
        state,
        contexts,
        quietContexts,
        new InventoryService(new SyntheticVehicleSource(new EfVehicleSource(contexts), targetCount), new EfPhotoManifestSource(contexts)),
        new BidService(new EfBidStore(contexts)),
        new EfActivityStore(quietContexts),
        async () =>
        {
            using var db = contexts.CreateDbContext();
            return await db.Vehicles.AnyAsync() && await db.Photos.AnyAsync();
        },
        // Identity's own store over the accounts tables, given a context from the
        // request's scope. The type is the one AddEntityFrameworkStores would have
        // registered for a user type with no roles; naming it here is what lets
        // the other backend register a different one under the same interface.
        services => new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserOnlyStore<YardUser, YardDbContext, string>(
            services.GetRequiredService<YardDbContext>(),
            services.GetService<IdentityErrorDescriber>()));
}
if (sqlState.Ready)
{
    sqlBackend.Attach(SqlAttachment(sqlState));
}
backendList.Add(sqlBackend);
// #endregion sql-backend

// #region cosmos-backend
// The document store, when there is an account to talk to. Same question, same
// answer shape, other store: are the containers there with the keys this code
// was written for, and is the seed in them. A refusal falls through to the
// file-backed catalogue exactly as a missing schema does on SQL Server.
CosmosStore? cosmos = null;
StoreSecondChance.Plan? cosmosSecondChance = null;
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
    var cosmosState = await cosmosStartup.Time("prepare", () => StorePrepare.WithTriesAsync(() => cosmos.PrepareAsync(seedVehicles, seedPhotos)));
    var store = cosmos;
    // The same shape as the relational backend: the files until the store is
    // attached, at once when it came up, by the second chance when it did not
    // (ADR: A second store on Cosmos DB, and what it costs).
    var cosmosBackend = new Backend
    {
        Key = "cosmos",
        Name = "Azure Cosmos DB",
        Database = cosmosState,
        Startup = cosmosStartup,
        Cosmos = cosmos,
        Inventory = new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
        Bids = new BidService(NullBidStore.Instance),
        Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
        Probe = () => Task.FromResult(false),
        UserStore = _ => null,
    };
    StoreAttachment CosmosAttachment(DatabaseState state) => new(
        state,
        null,
        null,
        // The same three ports, answered out of the document store.
        new InventoryService(new SyntheticVehicleSource(new CosmosVehicleSource(store), targetCount), new CosmosPhotoManifestSource(store)),
        new BidService(new CosmosBidStore(store)),
        new CosmosActivityStore(store),
        store.ProbeAsync,
        // One document per account and one per address, and none of Identity's
        // seven tables (ADR: Accounts on a document store).
        _ => new CosmosUserStore(store));
    if (cosmosState.Ready)
    {
        cosmosBackend.Attach(CosmosAttachment(cosmosState));
    }
    backendList.Add(cosmosBackend);
    cosmosSecondChance = new StoreSecondChance.Plan(
        cosmosBackend,
        () => store.PrepareAsync(seedVehicles, seedPhotos),
        async state =>
        {
            var attachment = CosmosAttachment(state);
            await attachment.Inventory.WarmAsync();
            await attachment.Bids.LoadAsync();
            cosmosBackend.Attach(attachment);
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
// Identity's stores want a context per request, and the factory hands out
// contexts rather than registering one. This is the adapter between the two
// and one of the two scoped registrations in the application. The factory is
// the backend's at call time, so a store attached after startup serves the
// accounts too; nothing asks for a context while the store is on the files,
// because the user store is null until then.
builder.Services.AddScoped(_ =>
    (sqlBackend.Contexts ?? throw new InvalidOperationException("the relational store is not attached"))
        .CreateDbContext());
// #region second-chance-wiring
// A store that refused at startup is asked again after it (ADR: The relational
// store, the addendum on the second chance): every thirty seconds for an hour,
// attached warm when it answers. Only the backends that are not ready are
// planned, so a site whose stores came up runs nothing here.
var secondChances = new List<StoreSecondChance.Plan>
{
    new(
        sqlBackend,
        () => YardDatabase.PrepareAsync(yard, seedVehicles, seedPhotos),
        async state =>
        {
            var attachment = SqlAttachment(state);
            await attachment.Inventory.WarmAsync();
            await attachment.Bids.LoadAsync();
            sqlBackend.Attach(attachment);
        }),
};
if (cosmosSecondChance is not null)
{
    secondChances.Add(cosmosSecondChance);
}
builder.Services.AddHostedService(services => new StoreSecondChance(secondChances, services.GetRequiredService<ILogger<StoreSecondChance>>()));
// #endregion second-chance-wiring
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
var requestLog = new RequestRingBuffer(RingSizes.RequestRing);
builder.Services.AddSingleton(logLog);
builder.Services.AddSingleton(requestLog);
builder.Services.AddSingleton(sqlLog);
builder.Services.AddSingleton<ISqlLog>(sqlLog);
builder.Services.AddSingleton(storeLog);
builder.Services.AddSingleton<IStoreLog>(storeLog);
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
builder.Services.AddSingleton<ICurrentRequest>(currentRequest);
builder.Logging.AddProvider(new RingBufferLoggerProvider(logLog));

// #region kept-logs-wiring
// The kept log (ADR: Logs that outlive the container): the same three things
// the rings hold, written to the document store off the request path and
// kept for three years. One collector, fed by the request hook below and by a
// logging provider that takes this application's warnings and errors, on a
// container with no document store configured it is wired to nothing and
// the card says so. The provider reads the store and the request a line
// belongs to from the current request when there is one.
ILogStore logStore = cosmos is not null ? new CosmosLogStore(cosmos) : NullLogStore.Instance;
// Reset links (ADR: Accounts and per-user bids, addendum of 14 September):
// the GUID a link carries is kept in the document store for the hour and
// forgotten on use; without one, in this process's memory, which is what
// the test host and a developer's machine get.
IResetLinks resetLinks = cosmos is not null ? new CosmosResetLinks(cosmos) : new MemoryResetLinks();
builder.Services.AddSingleton(resetLinks);
// This site as a visitor reaches it, for links written into an email.
// Behind the edge the request's own host is the origin, which is not an
// address anybody should be sent; unset, the request's host is used, which
// is right on a developer's machine and on the test host.
string? siteUrl = builder.Configuration["Site:Url"];
var logCollector = new LogCollector(logStore, builder.Configuration.GetValue("Logs:DrainSeconds", LogCollector.DefaultIntervalSeconds));
builder.Services.AddSingleton(logCollector);
builder.Services.AddHostedService(_ => logCollector);
// #region kept-rings-wiring
// The four public lists on the Admin tab are rings, and a roll empties them.
// Each entry a ring takes is also offered to the collector above, as the JSON
// the ring's own endpoint serves, so the cards can read a day, a week or a
// month back from the document store (ADR: Logs that outlive the container, the addendum on the cards). The
// two error rings are made further down and are wired where they are made.
var keptRings = new KeptRingWriter(logCollector, backends.Default.Key);
sqlLog.Kept = statement => keptRings.Keep(KeptRings.Sql, statement.At, statement);
storeLog.Kept = operation => keptRings.Keep(KeptRings.Store, operation.At, operation);
logLog.Kept = line => keptRings.Keep(KeptRings.Log, line.At, line);
builder.Services.AddSingleton(new KeptRingReader(logCollector, backends.Default.Key, backends.Named("cosmos")?.Name ?? backends.Default.Name));
// #endregion kept-rings-wiring
builder.Logging.AddProvider(new CollectorLoggerProvider(logCollector, () =>
{
    var http = httpContextAccessor.HttpContext;
    return http is null
        ? ("", "", "")
        : (backends.For(http).Key, http.Request.Path.HasValue ? http.Request.Path.Value! : "/", http.TraceIdentifier);
}));
// #endregion kept-logs-wiring

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
    "/api/admin/activity",
    "/api/admin/activity/visitors",
    "/api/admin/pages",
    "/api/admin/machines",
    "/api/admin/logs/kept",
    "/api/admin/kept",
    "/api/admin/reset-links",
    "/api/errors",
    "/api/health",
    "/readyz",
    "/healthz",
};

// The activity feature's two pieces, wired below once the signing key exists;
// the request hook reads them through these so it can be declared here beside
// the ring it sits next to (ADR: Site activity, and the line an address does
// not cross).
ActivityCollector? activityCollector = null;
VisitorTokens? visitorTokens = null;
// The loop that keeps every read warm (ADR: Kept awake), wired after the app is
// built; the health report reads it through this.
var keepWarmState = new KeepWarmState();
builder.Services.AddSingleton(keepWarmState);

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

    // The page sweep asks this container for every address it serves, at every
    // roll (ADR: Every page, checked at every roll). That is this container
    // talking to itself: left in, ninety self-requests would push a morning of
    // real traffic out of the ring and add a visitor to the activity card who
    // is this container.
    if (context.Request.Headers.ContainsKey(PageStatusRunner.CheckHeader))
    {
        return;
    }

    // The keep-warm loop's reads (ADR: Kept awake) stay out of the ring and the
    // kept log: two hundred slots of the container reading itself every four
    // minutes would be the speed tile timing the loop, not a visitor. They still
    // reach the activity card below, marked as the site reading itself.
    bool keptWarm = context.Request.Headers.ContainsKey(KeepWarm.Header);
    if (!keptWarm)
    {
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

    // #region activity-hook
    // And the same request as a hit for the activity card, offered to the
    // collector and forgotten: nothing here waits on a store. What the hit
    // carries, and what it cannot, is decided in Activity.cs and in the type.
    // The token and the network are made once here and shared with the kept
    // log below, so a request pays for one keyed hash and not two.
    if (visitorTokens is null || !Hits.Counts(path))
    {
        return;
    }

    string address = VisitorTokens.AddressOf(context);
    var at = DateTimeOffset.UtcNow;
    string token = visitorTokens.TokenFor(address, at);
    string network = VisitorTokens.NetworkOf(address);
    string store = backends.For(context).Key;
    activityCollector?.Offer(Hits.For(visitorTokens, address, at, token, network, path, store, context.Request.Headers.UserAgent.FirstOrDefault(), Hits.SourceOf(context.Request.Headers.Referer.FirstOrDefault(), path, context.Request.Host.Host)));
    // #endregion activity-hook

    // #region kept-logs-hook
    // And once more as a kept event: the same request, the same token and
    // network, with its method, status and duration, offered to the log
    // collector and forgotten. The page's own files and the photos are left
    // out for the reason the activity feature leaves them out.
    if (keptWarm)
    {
        return;
    }

    logCollector.Offer(LogEvents.Request(
        at,
        context.Request.Method,
        path,
        context.Response.StatusCode,
        (long)elapsed.TotalMilliseconds,
        store,
        token,
        network,
        context.TraceIdentifier));
    // #endregion kept-logs-hook
}
#endregion admin-rings

// #region auth
// Accounts (ADR: Accounts and per-user bids). Identity owns the password
// hashing, the normalised lookups and the account tables, which is the part
// worth not writing twice; the session is a JWT this service signs and reads
// itself, carried in a cookie the page cannot touch.
//
// The signing key is configuration. The deploy hands both containers the same
// one from a repository secret (Auth__SigningKey in the container spec, filled
// at roll time like the connection strings), so a session survives a roll and
// a token minted by one container reads on the other. Without a usable one,
// which is what a developer's machine, a test and a roll whose substitution
// failed all have, the process invents a random key and says so: every session
// ends with the process, and no key is ever committed (ADR: Three readers with
// no memory of the project).
string? configuredSigningKey = TokenIssuer.ConfiguredKey(builder.Configuration["Auth:SigningKey"]);
string signingKey = configuredSigningKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
// A year, renewed on every day it is used (Tokens.cs, the renewal region):
// a login lasts a year past the last visit, which is what "permanent" means
// for a cookie that has to expire somewhere (ADR: Accounts and per-user
// bids, addendum). Configuration, so a test can shorten it.
var tokens = new TokenIssuer(signingKey, TimeSpan.FromDays(builder.Configuration.GetValue("Auth:SessionDays", 365)));
builder.Services.AddSingleton(tokens);

// #region activity-wiring
// Site activity (ADR: Site activity, and the line an address does not cross).
// The visitor token is keyed with the signing key, so both containers turn one
// address into one token within a day and nobody outside can turn it back.
// The collector is a hosted service that writes off the request path, one
// batch every five seconds, to one keeper: Azure Cosmos DB wherever it came
// up, whichever store served the request, because it keeps the rows without
// expiry and because writing a serverless relational database every five
// seconds kept it from ever pausing and spent September's free amount on the
// fourteenth (ADR: Site activity, and the line an address does not cross,
// addendum). Without Cosmos DB the default store keeps its own rows. The
// admin key guards the visitor rows; unset, that endpoint is a 404, which is
// the default and the safe one.
visitorTokens = new VisitorTokens(signingKey);
var activityStores = backends.All.ToDictionary(backend => backend.Key, backend => backend.Activity, StringComparer.Ordinal);
var activityKeeper = backends.All.FirstOrDefault(backend => backend.Key == "cosmos" && backend.Activity is not NullActivityStore)?.Key ?? backends.Default.Key;
builder.Services.AddSingleton(services => new ActivityCollector(activityStores, activityKeeper, services.GetRequiredService<ILogger<ActivityCollector>>()));
builder.Services.AddHostedService(services => services.GetRequiredService<ActivityCollector>());

// #region machine-sampler-wiring
// Memory is not an event, so nothing on the Admin tab could show it until
// something asked on a clock (ADR: What the machines are doing). Four an
// hour for an hour, in about twenty kilobytes of this container's memory.
builder.Services.AddSingleton(new MachineSampler(RingSizes.MachineSamples));
builder.Services.AddHostedService(services => services.GetRequiredService<MachineSampler>());
// #endregion machine-sampler-wiring
// #region machine-history-wiring
// And a minute at a time is kept where a roll cannot empty it, so the card
// can draw a day, a week and a month beside the hour this process remembers
// (ADR: What the machines are doing, the addendum on the windows). Both sites
// write to the one document store, each under its own name; with no document
// store the port is wired to nothing, the recorder idles, and the card says
// so. The relational reading goes through the quiet context, so a read a
// minute is not the newest line on the SQL card for ever.
IMachineHistory machineHistory = cosmos is not null ? new CosmosMachineHistory(cosmos) : NullMachineHistory.Instance;
builder.Services.AddSingleton(new MachineHistoryReader(machineHistory, backends.Default.Key));
builder.Services.AddSingleton(services => new MachineRecorder(
    services.GetRequiredService<MachineSampler>(),
    machineHistory,
    backends.Default.Key,
    cancellation => ResourceStats.ReadAsync(sqlBackend.QuietContexts, "the relational store", 8, cancellation),
    () => storeLog.Snapshot(),
    () => requestLog.Snapshot(),
    services.GetRequiredService<ILogger<MachineRecorder>>()));
builder.Services.AddHostedService(services => services.GetRequiredService<MachineRecorder>());
// #endregion machine-history-wiring
// The catalogue of the store this site does not serve is given back once
// nobody has asked for it in a while. Zero minutes, the default, is never;
// the plan both sites share sets ten (ADR: One plan, two sites).
builder.Services.AddSingleton(services => new CatalogueKeeper(
    backends,
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Store:ReleaseIdleMinutes", 0)),
    services.GetRequiredService<ILogger<CatalogueKeeper>>()));
builder.Services.AddHostedService(services => services.GetRequiredService<CatalogueKeeper>());
var adminKey = new AdminKey(builder.Configuration["Admin:Key"]);
builder.Services.AddSingleton(adminKey);
// Whether the per-visitor rows (the visitor table and the kept log) are
// served at all. Off by default on Steve's word of 13 September ("disable
// the per visitor data for now"): the rows keep being written, the two
// endpoints answer 404 to everybody, key or no key, and the cards do not
// show. Admin__VisitorRows=true turns it back on (ADR: Site activity, and
// the line an address does not cross, sixth addendum).
bool visitorRows = builder.Configuration.GetValue("Admin:VisitorRows", false);
builder.Services.AddSingleton(new AdminSettings(visitorRows));
// A report is kept thirty seconds and rebuilt behind the next read for ten
// minutes after that, so a reader never waits on the visitor rows being counted
// (ActivityReportCache, the addendum of 28 September).
builder.Services.AddSingleton(new ActivityReportCache(TimeProvider.System));

// #region email-wiring
// The one email this site sends, the reset link, through Azure Communication
// Services as the containers' own identity; two plain settings and no key.
// Unset, the "Forgot password" endpoint says so and the operator's link from
// the Admin tab is the way (ADR: Accounts and per-user bids, addendum).
builder.Services.AddSingleton<IEmailSender>(services => AcsEmailSender.FromConfiguration(
    builder.Configuration["Email:Endpoint"],
    builder.Configuration["Email:From"],
    // The same identity the stores use, chosen by the same setting.
    () => CosmosStore.CredentialFor(
        builder.Configuration["Cosmos:Credential"] ?? "azure-cli",
        builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5"),
    services.GetRequiredService<ILogger<AcsEmailSender>>()));
builder.Services.AddSingleton(new ForgotLimit(ForgotLimit.DefaultSpacing, () => DateTimeOffset.UtcNow));
// #endregion email-wiring
// #endregion activity-wiring

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

#region api-document
// The API's description of itself (ADR: The API describes itself): one
// document built from the endpoints below as they are mapped, the operator
// surface filtered out of it, and the two schemes a session travels by
// declared. The version is the build's, because that is the honest number.
builder.Services.AddOpenApi(ApiDocument.Name, options => ApiDocument.Configure(options, buildVersion, buildCommit, siteUrl));
#endregion api-document

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
builder.Services.AddSingleton(telemetry);
#endregion telemetry

// Identifiers, not secrets: the identity's client id and this group's ARM path.
var azureSelf = new AzureSelf(
    builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
    builder.Configuration["Azure:SelfResourceId"]
        ?? "/subscriptions/df3b718c-6d99-4904-8102-6f865941f640/resourceGroups/RG-THEYARD-SS/providers/Microsoft.ContainerInstance/containerGroups/aci-theyard-ss");
builder.Services.AddSingleton(azureSelf);
// The other container's metrics, read server side with a short patience (ADR: Backends, side by side).
var peer = new PeerReader(
    builder.Configuration["Peer:Url"],
    new HttpClient { Timeout = PeerReader.Patience + TimeSpan.FromSeconds(1) });
builder.Services.AddSingleton(peer);
// The performance proof (ADR: Same performance, proven), built on first use so a test's own
// ProofClients registration is the one it gets.
builder.Services.AddSingleton(services => new ProofRunner(backends, services.GetRequiredService<ProofClients>(), sqlLog, storeLog));
// The sweep over every address this container serves (ADR: Every page, checked at every roll):
// it dials the loopback once the server is listening, and is started at the roll further down.
builder.Services.AddSingleton(services => new PageStatusRunner(
    () => SelfAddress.Of(services) is { } dialable
        ? new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(dialable),
            Timeout = TimeSpan.FromSeconds(30),
        }
        : null,
    // The frontend is in the image and not in a checkout, so the addresses it
    // serves are checked where they exist and named nowhere else.
    () => services.GetRequiredService<IWebHostEnvironment>() is { } environment
        && !string.IsNullOrEmpty(environment.WebRootPath)
        && File.Exists(Path.Combine(environment.WebRootPath, "index.html")),
    buildVersion,
    buildCommit));

// The two error rings and the moment the process started, registered before the
// host exists so the endpoints can ask for them; the rings are wired to the kept
// log and the start is marked further down, where they always were.
var errorRings = new ErrorRings();
builder.Services.AddSingleton(errorRings);
var hostStart = new HostStart();
builder.Services.AddSingleton(hostStart);

var app = builder.Build();
activityCollector = app.Services.GetRequiredService<ActivityCollector>();

// Entity Framework's own command log goes where every other log line goes,
// which the Admin tab's log section and a test both rely on; the document
// store writes one line per operation to the same place from here on
// (ADR: What the store is actually doing, addendum).
hostLoggers = app.Services.GetRequiredService<ILoggerFactory>();
(sqlBackend.Contexts as ContextFactory)?.Attach(hostLoggers);
if (cosmos is not null)
{
    cosmos.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosStore>();
}
// The promise the accounts record makes: an invented signing key is said out
// loud, because its consequence is every session ending when this process does
// (ADR: Accounts and per-user bids). Nothing about the key itself is logged.
if (configuredSigningKey is null)
{
    app.Logger.LogWarning(
        "No usable Auth:SigningKey is configured (none, a placeholder, or under {Bytes} bytes); "
        + "this process signs cookies with a key it invented at startup, so every session ends with it",
        TokenIssuer.MinimumKeyBytes);
}

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

// #region session-renewal
// A signed-in request carries its session forward: once a day, the first
// request that arrives with a token more than a day into its life gets a
// fresh cookie with the same claims and a fresh year. Before the endpoint
// runs, because a cookie has to be set before the response starts; only on
// the API, where the token is read; never on the way out, where the cookie
// is being deleted.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/api/auth/logout")
        && context.User.Identity?.IsAuthenticated == true
        && tokens.ShouldRenew(context.User.FindFirst("exp")?.Value, DateTimeOffset.UtcNow))
    {
        string? id = context.UserIdOrNull();
        string? email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        string? store = context.User.FindFirst(TokenIssuer.StoreClaim)?.Value;
        if (id is not null && email is not null && store is not null)
        {
            context.Response.Cookies.Append(TokenIssuer.CookieName, tokens.Issue(id, email, store), TokenIssuer.CookieFor(context, tokens.Lifetime));
        }
    }

    await next();
});
// #endregion session-renewal

// #region warm-before-reading
// And before any endpoint reads a store, that store's catalogue is loaded, so
// no request thread is ever blocked on a load in progress (Warmth, in
// Stores.cs). Only the API: the page's files do not have a store.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        await Warmth.EnsureAsync(context.RequestServices.GetRequiredService<CurrentBackend>().Backend);
    }
    await next();
});
// #endregion warm-before-reading

// The dataset is snake_case; keep the wire shape identical to the source file.
var wireFormat = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

// #region api-document-routes
// The document at /api/openapi/v1.json and the reference page at /api/reference,
// served by this container like every other document here (ADR: The API
// describes itself). Under /api rather than at the framework's default
// /openapi, because /api is the one prefix the development server proxies to
// this host: at the default address the page's own request for its document
// came back as index.html on every developer's machine and in the browser
// suite, and worked only in the container. The page's script comes from the
// package, not a content delivery network, and the fonts, telemetry, AI chat
// and MCP link it would reach out for are off; it stays in the one light theme.
app.MapOpenApi("/api/openapi/{documentName}.json");
app.MapScalarApiReference(ApiDocument.ReferenceRoute, options => options
    .WithTitle(ApiDocument.Title)
    .WithFavicon(ApiDocument.Favicon)
    .WithOpenApiRoutePattern("/api/openapi/{documentName}.json")
    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
    .AddPreferredSecuritySchemes(ApiDocument.BearerScheme)
    .DisableDefaultFonts().DisableTelemetry().DisableAgent().DisableMcp().HideDeveloperTools()
    .ForceLightMode().HideDarkModeToggle().WithCustomCss(ApiDocument.ReferenceCss));
// #endregion api-document-routes
#endregion composition

// The catalogue: search, the filters' values, one vehicle (Endpoints/VehicleEndpoints.cs).
app.MapVehicleEndpoints();

// Bidding, the caller's standing, the room and the start-over (Endpoints/BidEndpoints.cs).
app.MapBidEndpoints();

// Documents: every markdown the sidebar can open, the pictures and diagrams
// they carry, the Bicep and the resume (Endpoints/DocsEndpoints.cs).
app.MapDocsEndpoints();

// ---------------------------------------------------------------------------
// Build provenance - the version and commit this container was built from,
// baked in as environment variables by the Docker build (ADR-005).
// ---------------------------------------------------------------------------

// The running build, liveness, readiness and the health report (Endpoints/HealthEndpoints.cs).
app.MapHealthEndpoints();


// ---------------------------------------------------------------------------
// Observability (ADR-010, roughed in 2026-09-01). Three surfaces feed the
// Admin tab: hand-rolled health checks, an in-memory error ring buffer, and
// the container group's own state read from Azure with its managed identity.
// ---------------------------------------------------------------------------

hostStart.Mark();
var startedAt = hostStart.At;
var errorLog = errorRings.Server;
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
var browserErrors = errorRings.Browser;
// #endregion two-rings
errorLog.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);
browserErrors.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);

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
        // The type and the frames, never the message: the frames are source
        // locations this repository publishes, and the message is not
        // (ADR: Error handling, the addendum on frames).
        errorLog.Record(context.Request.Path, 500, ex.GetType().Name, StackFrames.Of(ex));
        throw;
    }
});
#endregion error-log



// Recent errors, a browser's report of one, and the failure on purpose (Endpoints/ErrorEndpoints.cs).
app.MapErrorEndpoints();

// The Admin tab's reads and the operator's actions (Endpoints/AdminEndpoints.cs).
app.MapAdminEndpoints();



// Accounts and the password reset (Endpoints/AccountEndpoints.cs).
app.MapAccountEndpoints();

// #region page-status-wiring
// The sweep over every address this container serves. It dials the loopback
// the proof dials, and it is null until the server is listening, which is what
// keeps it from running under the test host: the suite drives RunAsync with
// the test server's own client instead (ADR: Every page, checked at every roll).
var pageStatus = app.Services.GetRequiredService<PageStatusRunner>();

// Every roll carries a check of the thing that was just rolled. The address
// is read from the server when the sweep runs rather than from the variable
// the proof's callback fills: these callbacks run in the reverse of the order
// they were registered, so this one runs first, and the first cut of it never
// swept anything (SelfAddress, and the browser suite that found it).
app.Lifetime.ApplicationStarted.Register(() => pageStatus.TryStart("roll"));
// And once more when the process has settled (1.0.3.8): the roll's sweep runs
// while the catalogues are warming, and a reading taken then is a reading of
// the start, not of the site. The second is what the tile shows until
// somebody asks for another.
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    await Task.Delay(PageStatusRunner.SecondSweep);
    pageStatus.TryStart("settled");
}));

// #region keep-warm-wiring
// Every public read, on both stores, every four minutes for as long as the
// container runs (ADR: Kept awake). On where App Service runs the site
// (WEBSITE_SITE_NAME is App Service's own) and off everywhere else, the test
// host included; KeepWarm:Enabled says otherwise either way. It dials the
// loopback the page sweep dials, through the whole pipeline.
if (builder.Configuration.GetValue("KeepWarm:Enabled", !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))))
{
    var keepWarmLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<KeepWarm>();
    HttpClient? keepWarmClient = null;
    var loop = new KeepWarm(
        async cancellation =>
        {
            keepWarmClient ??= SelfAddress.Of(app.Services) is { } dialable
                ? new HttpClient(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(dialable),
                    Timeout = TimeSpan.FromSeconds(30),
                }
                : null;
            return keepWarmClient is null
                ? new KeepWarmPass(DateTimeOffset.UtcNow, 0, 0, 0, null)
                : await KeepWarmReads.RunAsync(keepWarmClient, backends.All.Select(backend => backend.Key).ToList(), TimeProvider.System, keepWarmLog, cancellation);
        },
        TimeProvider.System,
        KeepWarm.RandomStagger(),
        keepWarmLog);
    keepWarmState.Loop = loop;
    app.Lifetime.ApplicationStarted.Register(() => _ = loop.StartAsync(CancellationToken.None));
    app.Lifetime.ApplicationStopping.Register(() => loop.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
}
// #endregion keep-warm-wiring







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

#region records-and-test-hook


// Exposes the entry point to WebApplicationFactory for integration tests.
public sealed partial class Program;
#endregion records-and-test-hook
