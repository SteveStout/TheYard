using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// Brings both stores up before anything is registered, because the answer decides
/// what gets registered: the relational store on SQL Server or SQLite, the document
/// store when there is an account, each behind the same ports, and the second chance
/// for a store that refused at startup (ADR: One container, both stores). What it made
/// is registered here and handed back, so Program.cs passes each later step only the
/// parts it needs.
/// </summary>
public static class StoreRegistration
{
    /// <summary>Prepares and registers both stores, their backends and the second chance, and answers what it made.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="paths">Where the dataset and the photo manifest are.</param>
    public static async Task<PreparedStores> AddTheYardStoresAsync(this WebApplicationBuilder builder, HostPaths paths)
    {
        string dataPath = paths.DataPath;
        string manifestPath = paths.ManifestPath;
        // The 200-record seed dataset is deterministically expanded to TargetCount
        // synthetic records (default 100,000): scale testing without a giant file.
        int targetCount = builder.Configuration.GetValue("Inventory:TargetCount", 100_000);
        #region persistence
        // SQLite through EF Core (ADR: The relational store). The connection string is
        // configuration, and without one this process gets a scratch file it deletes on
        // the way out: that is what every test wants, and it is a better answer for a
        // misconfigured deploy than writing, unannounced, somewhere nobody will look.
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
        // keys, and the container authenticates as its managed identity. Beside the
        // relational store, not instead of it: a container with both settings runs both
        // (ADR: One container, both stores). The same placeholder rule as the SQL
        // setting: a failed substitution reads as "no Cosmos DB here", never an address.
        string? configuredCosmos = builder.Configuration["Cosmos:AccountEndpoint"];
        bool cosmosConfigured = !string.IsNullOrWhiteSpace(configuredCosmos)
            && !configuredCosmos.StartsWith("__", StringComparison.Ordinal);
        // The names this process answers to (SiteHosts.cs) pick a request's store. With neither
        // name: Store:Default where set, else the relational store for the web app serving both
        // sites, else the document store for a run that configured it and said nothing else.
        var siteHosts = SiteHosts.From(builder.Configuration);
        bool servesBothSites = siteHosts.Sql is not null && siteHosts.Cosmos is not null;
        string? configuredDefaultStore = builder.Configuration["Store:Default"];
        string defaultStore = configuredDefaultStore ?? (servesBothSites || !cosmosConfigured ? "sql" : "cosmos");
        bool keepCatalogues = builder.Configuration.GetValue("Catalogue:Keep", false);
        string scratchDatabase = Path.Combine(Path.GetTempPath(), $"theyard-scratch-{Guid.NewGuid():N}.db");
        // Pooled, so SQLite's page cache outlives a statement (ADR: The store does the work).
        // Startup empties this one file's pool before deleting it, never ClearAllPools, which
        // would pull connections from under the other applications a test run holds at once.
        string databaseConnection = configuredDatabase ?? $"Data Source={scratchDatabase}";
        // The relational store is always one of the two: SQL Server when the deploy
        // gave one, SQLite otherwise, and "yard" keeps its name because most of this
        // file only ever needs the relational side's answer.
        var yard = YardConnection.Choose(configuredSqlServer, databaseConnection);
        // The two stores' rings for the Admin tab, made before either store so the cold start's
        // checks, seed and statements are the first lines in them (ADR: What the store is
        // actually doing) and (ADR: What the database is actually doing).
        var storeLog = new StoreRingBuffer(200);
        var sqlLog = new SqlRingBuffer(200);
        // The request describer, built by hand because the interceptor that feeds the SQL ring
        // exists before the container does; registered later so everything shares this one.
        var httpContextAccessor = new HttpContextAccessor();
        var currentRequest = new HttpCurrentRequest(httpContextAccessor);
        // The host's loggers, attached to a context factory made after startup too (StoreAnnouncement).
        var loggers = new StoreLoggers();
        #endregion persistence

        #region migrate-and-seed
        // The schema and the contents, before anything is registered, because the
        // answer decides what gets registered. Migrate rather than EnsureCreated: the
        // schema's history is files in this repository, so a container starting against
        // an older database brings it forward instead of finding a shape it half
        // recognises. A fresh database gets its contents from the JSON readers, so
        // `npm run data` regenerates the dataset and the seed cannot drift from it.
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
        // want a context for one operation, and there is no request scope at startup
        // when the catalogue is read. The interceptor puts every statement on the Admin
        // tab; it is attached here so YardConnection stays a description of where the
        // database is.
        // How long the first of the five asks waits after a refusal (two seconds unless the
        // configuration shortens it, which only a test that proves the fallback does).
        var firstWait = StorePrepare.FirstWaitFrom(builder.Configuration);
        var sqlStartup = new StartupTimings();
        var sqlState = await sqlStartup.Time("prepare", () => StorePrepare.WithTriesAsync(() => YardDatabase.PrepareAsync(yard, seedVehicles, seedPhotos), firstWait));
        // The backend stands on the files until its store is attached: the same two
        // ports answered out of the JSON, the null bid store, no accounts, with the
        // synthetic scale-up still decorating the vehicle source (ADR: The relational
        // store). Attached at once below when the store came up, or by the second
        // chance when it comes up later (ADR: The relational store, the addendum on
        // the second chance).
        var sqlBackend = new Backend
        {
            Key = "sql",
            Name = yard.Describe(),
            Database = sqlState,
            Startup = sqlStartup,
            Inventory = new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
            Bids = new BidService(new MemoryBidStore()),
            Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
            Probe = () => Task.FromResult(false),
            UserStore = _ => null,
        };
        // Everything that stands on the relational store, built from a state that
        // says it came up. The loggers are attached the moment the host exists (the
        // factory is read at call time, so an attach after startup gets them too).
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
            if (loggers.Factory is not null)
            {
                contexts.Attach(loggers.Factory);
            }
            return new StoreAttachment(
                state,
                contexts,
                quietContexts,
                Catalogues.Sql(contexts, yard, targetCount, Catalogues.UpkeepFor(keepCatalogues, defaultStore, "sql", servesBothSites)),
                new BidService(new EfBidStore(contexts)),
                new EfActivityStore(quietContexts),
                () => YardDatabase.HasSeedAsync(contexts),
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
                AzureIds.ClientId(builder.Configuration),
                storeLog);
            // Every operation is filed under the request that caused it, the first one included.
            cosmos.CurrentRequest = currentRequest;
            var cosmosStartup = new StartupTimings();
            var cosmosState = await cosmosStartup.Time("prepare", () => StorePrepare.WithTriesAsync(() => cosmos.PrepareAsync(seedVehicles, seedPhotos), firstWait));
            var store = cosmos;
            // The relational backend's shape: the files until the store is attached (ADR: A
            // second store on Cosmos DB, and what it costs).
            var cosmosBackend = new Backend
            {
                Key = "cosmos",
                Name = "Azure Cosmos DB",
                Database = cosmosState,
                Startup = cosmosStartup,
                Cosmos = cosmos,
                Inventory = new InventoryService(new SyntheticVehicleSource(seedVehicles, targetCount), seedPhotos),
                Bids = new BidService(new MemoryBidStore()),
                Market = new MarketService(builder.Configuration.GetValue("Market:GraceSeconds", MarketService.DefaultGraceSeconds)),
                Probe = () => Task.FromResult(false),
                UserStore = _ => null,
            };
            StoreAttachment CosmosAttachment(DatabaseState state) => new(
                state,
                null,
                null,
                // The same three ports, answered out of the document store.
                Catalogues.Cosmos(store, targetCount, Catalogues.UpkeepFor(keepCatalogues, defaultStore, "cosmos", servesBothSites)),
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

        // Which one a request gets: its header, the name it arrived on, or the default (ADR: One
        // web app for every .NET process). Peer:Site is unset where one process serves both sites.
        var backends = new Backends(
            backendList,
            defaultStore,
            builder.Configuration["Peer:Site"],
            siteHosts);
        builder.Services.AddSingleton(siteHosts);
        builder.Services.AddSingleton(backends);
        builder.Services.AddScoped<CurrentBackend>();
        // Identity's stores want a context per request and the factory hands out contexts, so this
        // adapts one to the other (one of the four scoped registrations). The factory is read at
        // call time, so a store attached after startup serves the accounts too.
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
        // The partition key experiment needs the document store; without one it says so.
        builder.Services.AddSingleton<IStoreExperiment>(
            cosmos is not null ? new PartitionExperiment(cosmos, TimeProvider.System) : NoStoreExperiment.Instance);
        #endregion migrate-and-seed
        var prepared = new PreparedStores(
            backends,
            sqlBackend,
            cosmos,
            new ScratchDatabase(configuredDatabase is null && yard.Provider == YardProvider.Sqlite ? scratchDatabase : null),
            sqlLog,
            storeLog,
            httpContextAccessor,
            currentRequest,
            loggers);
        builder.Services.AddSingleton(prepared.Scratch);
        builder.Services.AddSingleton(new DocumentStore(cosmos));
        builder.Services.AddSingleton(new RelationalStore(sqlBackend));
        builder.Services.AddSingleton(loggers);
        return prepared;
    }
}
