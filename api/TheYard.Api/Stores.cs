using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

// #region backend
/// <summary>
/// One store and everything that stands on it: the catalogue loaded from it,
/// the bids replayed out of it, the room bidding against those, the accounts
/// kept in it, and the numbers from bringing it up (ADR: One container, both
/// stores). A container holds one of these per store it is configured for,
/// and a request is served by exactly one of them.
///
/// <para>Nothing in here is shared between stores. Two catalogues cost twice
/// the memory, which the record prices, and buy the one thing the comparison
/// needs: a listing served by the Cosmos DB backend was loaded from Cosmos DB,
/// and the cold start beside it is that store's own.</para>
/// </summary>
public sealed class Backend
{
    /// <summary>The short name the header and the bar use: "sql" or "cosmos".</summary>
    public required string Key { get; init; }

    /// <summary>The store as it describes itself: "Azure SQL Database", "SQLite", "Azure Cosmos DB".</summary>
    public required string Name { get; init; }

    public required DatabaseState Database { get; set; }

    public required StartupTimings Startup { get; init; }

    public required InventoryService Inventory { get; set; }

    public required BidService Bids { get; set; }

    public required MarketService Market { get; init; }

    /// <summary>The document store, on the Cosmos DB backend only.</summary>
    public CosmosStore? Cosmos { get; init; }

    /// <summary>The relational contexts, on the SQL backend only, and only once it has come up.</summary>
    public IDbContextFactory<YardDbContext>? Contexts { get; set; }

    /// <summary>The same database without the statement interceptor, for the readings a minute that must not fill the SQL log with themselves.</summary>
    public IDbContextFactory<YardDbContext>? QuietContexts { get; set; }

    /// <summary>
    /// Where this store keeps the requests it served, for the Admin tab's
    /// activity card (ADR: Site activity, and the line an address does not
    /// cross). The null store when the backend did not come up: nothing kept,
    /// and the card says so.
    /// </summary>
    public IActivityStore Activity { get; set; } = NullActivityStore.Instance;

    /// <summary>Is the seed catalogue in the store right now. Two reads, timed by the health check.</summary>
    public required Func<Task<bool>> Probe { get; set; }

    /// <summary>
    /// Identity's store over this backend's accounts, built per request from
    /// the request's scope, or null when the store did not come up and there
    /// are no accounts to keep.
    /// </summary>
    public required Func<IServiceProvider, IUserStore<YardUser>?> UserStore { get; set; }

    /// <summary>
    /// Whether this backend keeps accounts and bids across a restart. False is
    /// the fallback the relational store record describes: the catalogue is
    /// served from files, the bidding works, and nothing outlives the process.
    /// </summary>
    public bool Ready => Database.Ready;

    // #region attach
    /// <summary>
    /// The store, attached: everything that stands on it replaced in one call
    /// (ADR: The relational store, the addendum on the second chance). At
    /// startup this is called at once when the store came up; after startup
    /// the second chance calls it with a catalogue it has already warmed, so
    /// the first request after the swap is not the cold one. Each member is
    /// one reference written once, and a request in flight sees either the
    /// files or the store for each of them, both of which answer correctly;
    /// the swap happens at most once in a process's life.
    /// </summary>
    public void Attach(StoreAttachment attachment)
    {
        Database = attachment.Database;
        Contexts = attachment.Contexts;
        QuietContexts = attachment.QuietContexts;
        Inventory = attachment.Inventory;
        Bids = attachment.Bids;
        Activity = attachment.Activity;
        Probe = attachment.Probe;
        UserStore = attachment.UserStore;
    }
    // #endregion attach
}

/// <summary>What a store brings with it when it is attached to its backend: the state that says it came up, and the five things that stand on it.</summary>
public sealed record StoreAttachment(
    DatabaseState Database,
    IDbContextFactory<YardDbContext>? Contexts,
    IDbContextFactory<YardDbContext>? QuietContexts,
    InventoryService Inventory,
    BidService Bids,
    IActivityStore Activity,
    Func<Task<bool>> Probe,
    Func<IServiceProvider, IUserStore<YardUser>?> UserStore);
// #endregion backend

// #region five-tries
/// <summary>
/// A store is asked five times at startup before it is written off (Steve,
/// 2026-09-23: "we should always retry 5 times with SQL and Cosmos, can we add
/// some retry logic before we log it as a error?"). The waits between asks
/// double from two seconds, about thirty seconds in all, which fits inside
/// the deploy's five minutes with the container's own start-up still to pay
/// for. The state that comes back says how many asks it took, so the log line
/// and the Admin tab's startup card carry the count; the error is logged only
/// after the fifth refusal, by the caller, as it always was.
/// </summary>
public static class StorePrepare
{
    public const int Tries = 5;
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(2);

    public static Task<DatabaseState> WithTriesAsync(Func<Task<DatabaseState>> prepare) =>
        WithTriesAsync(prepare, Task.Delay);

    public static async Task<DatabaseState> WithTriesAsync(
        Func<Task<DatabaseState>> prepare,
        Func<TimeSpan, Task> wait)
    {
        DatabaseState state = new(false, "never asked");
        var between = FirstWait;
        for (int attempt = 1; attempt <= Tries; attempt++)
        {
            try
            {
                state = await prepare();
            }
            catch (Exception ex)
            {
                // Prepare answers a state rather than throwing; this is the belt to that brace.
                state = new DatabaseState(false, ex.GetType().Name, ex);
            }

            if (state.Ready)
            {
                return attempt == 1 ? state : Renamed(state, $"{state.Note}, on ask {attempt} of {Tries}");
            }

            if (attempt < Tries)
            {
                await wait(between);
                between *= 2;
            }
        }

        return Renamed(state, $"{state.Note}, refused {Tries} times");
    }

    /// <summary>The same state with the count in its note; the note is read-only by design, so this is the constructor again.</summary>
    private static DatabaseState Renamed(DatabaseState state, string note) => new(state.Ready, note, state.Failure)
    {
        SchemaMs = state.SchemaMs,
        SeedMs = state.SeedMs,
        SeedRequestUnits = state.SeedRequestUnits,
    };
}
// #endregion five-tries

// #region second-chance
/// <summary>
/// A store that did not come up at startup is tried again (ADR: The relational
/// store, the addendum on the second chance). Until 1.0.3.6 the prepare ran
/// once, before the container was built, and a transient at that second cost
/// the store for the life of the process: both containers came up from the
/// 1.0.3.4 roll with Azure SQL Database unavailable and served the catalogue
/// from files for the rest of the day, with sign-in and bids on that site
/// going nowhere. Now a backend whose store refused is asked again every
/// <see cref="Every"/> for <see cref="Window"/>, and when the store answers
/// the backend is attached to it, warm. A store that is genuinely gone is
/// still the fallback the relational store record describes, and this stops
/// asking after the window so a missing database is not a query a minute for
/// ever.
/// </summary>
public sealed class StoreSecondChance(
    IReadOnlyList<StoreSecondChance.Plan> plans,
    ILogger<StoreSecondChance> logger) : BackgroundService
{
    /// <summary>One backend that did not come up: how to try the store again, and what to do when it answers.</summary>
    public sealed record Plan(Backend Backend, Func<Task<DatabaseState>> Prepare, Func<DatabaseState, Task> Attach);

    public static readonly TimeSpan Every = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var plan in plans.Where(plan => !plan.Backend.Ready))
        {
            _ = TryAsync(plan, Every, Window, Task.Delay, logger, stoppingToken);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// The loop, on its own so a test can run it with a fake clock: prepare,
    /// and on the first state that is ready, attach and stop; otherwise wait
    /// and ask again until the window closes. Returns how many times the store
    /// was asked. The prepare never throws (it answers a state), but the
    /// attach can, and an attach that throws is logged and the loop goes on,
    /// because a store that answered once will answer again.
    /// </summary>
    public static async Task<int> TryAsync(
        Plan plan,
        TimeSpan every,
        TimeSpan window,
        Func<TimeSpan, CancellationToken, Task> wait,
        ILogger logger,
        CancellationToken cancellation)
    {
        int attempts = 0;
        var started = DateTimeOffset.UtcNow;
        var deadline = started + window;
        while (!cancellation.IsCancellationRequested && !plan.Backend.Ready)
        {
            try
            {
                await wait(every, cancellation);
            }
            catch (OperationCanceledException)
            {
                return attempts;
            }

            attempts++;
            DatabaseState state;
            try
            {
                state = await plan.Prepare();
            }
            catch (Exception ex)
            {
                // Prepare answers a state rather than throwing; this is the belt to that brace.
                state = new DatabaseState(false, $"{plan.Backend.Name}: {ex.GetType().Name}", ex);
            }

            if (state.Ready)
            {
                try
                {
                    await plan.Attach(state);
                    logger.LogInformation(
                        "The {Store} store came up on the second chance, attempt {Attempt}: {Note}",
                        plan.Backend.Name, attempts, state.Note);
                    return attempts;
                }
                catch (Exception ex)
                {
                    // The type only; the message can carry a host name and this line reaches the public log section.
                    logger.LogError("Attaching the {Store} store failed with {Exception} on attempt {Attempt}; it will be asked again", plan.Backend.Name, ex.GetType().Name, attempts);
                }
            }
            else
            {
                logger.LogWarning(
                    "The {Store} store still refused on attempt {Attempt} of the second chance: {Note}",
                    plan.Backend.Name, attempts, state.Note);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogError(
                    "The {Store} store did not come up in {Window}; the catalogue stays on the files until the next roll",
                    plan.Backend.Name, window);
                return attempts;
            }
        }
        return attempts;
    }
}
// #endregion second-chance

// #region backends
/// <summary>
/// The stores this container runs, and which one a request gets.
///
/// <para>The choice is a header, which is how a measurement asks for a store
/// without a browser; a request that names none gets the container's default,
/// which is what makes each site one store's site (ADR: One container, both
/// stores, the addendum on the toggle moving to the sites). A header naming a
/// store this container does not have is the default, never an error.</para>
///
/// <para>Until 1.0.0.101 the toggle at the top of the page set a cookie
/// instead, and a visitor from those versions may still carry it for a year.
/// It no longer chooses anything, and the stores endpoint expires it on
/// sight, so a browser that toggled last week lands where the address bar
/// says rather than on a store the page can no longer switch away from.</para>
/// </summary>
public sealed class Backends
{
    /// <summary>The cookie the toggle used to set. Read only to be expired.</summary>
    public const string LegacyCookieName = "yard-store";
    public const string HeaderName = "X-Yard-Store";

    private readonly Backend[] _all;

    public Backends(IEnumerable<Backend> all, string? defaultKey, string? otherSite = null)
    {
        _all = all.ToArray();
        if (_all.Length == 0)
        {
            throw new ArgumentException("a container needs at least one store", nameof(all));
        }

        Default = Named(defaultKey) ?? _all[0];
        OtherSite = Uri.TryCreate(otherSite, UriKind.Absolute, out var site)
            && (site.Scheme == Uri.UriSchemeHttp || site.Scheme == Uri.UriSchemeHttps)
            ? site.GetLeftPart(UriPartial.Authority)
            : null;
    }

    public IReadOnlyList<Backend> All => _all;

    /// <summary>
    /// The other site, the container whose default is the other store, as a
    /// visitor should reach it (`Peer:Site`), so the bar can put both
    /// addresses on every page. The peer endpoint reads the other container
    /// by its origin; this is the address a person types, which behind the
    /// edge is a different one. Null when there is no other site, and only
    /// ever an http or https origin, so a setting cannot put anything else in
    /// a link.
    /// </summary>
    public string? OtherSite { get; }

    /// <summary>The backend a request gets when it names none.</summary>
    public Backend Default { get; }

    public Backend? Named(string? key) =>
        key is null ? null : Array.Find(_all, backend => string.Equals(backend.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The backend a request asked for: the header, or the default.</summary>
    public Backend For(HttpContext? context)
    {
        if (context is null)
        {
            return Default;
        }
        if (context.Request.Headers.TryGetValue(HeaderName, out var header) && Named(header) is { } byHeader)
        {
            return byHeader;
        }
        return Default;
    }

    /// <summary>
    /// Expire the toggle's old cookie if this request carries one. The page
    /// asks for the stores on every load, so one visit is enough to clean a
    /// browser that toggled on 1.0.0.94 to 1.0.0.100. Nothing else reads it.
    /// </summary>
    public static void ExpireLegacyCookie(HttpContext context)
    {
        if (context.Request.Cookies.ContainsKey(LegacyCookieName))
        {
            context.Response.Cookies.Delete(LegacyCookieName, new CookieOptions { Path = "/" });
        }
    }

    /// <summary>What the page is told about the stores, with the one this request would get marked.</summary>
    public StoresView Describe(HttpContext? context)
    {
        var current = For(context);
        return new StoresView(
            current.Key,
            _all.Select(backend => new StoreView(
                backend.Key,
                backend.Name,
                backend.Ready,
                ReferenceEquals(backend, Default))).ToArray(),
            OtherSite);
    }
}

/// <summary>
/// The backend serving this request, resolved once per request from the
/// request's own headers. Endpoints ask this rather than the
/// container, so the same handler serves whichever store the request named.
/// </summary>
public sealed class CurrentBackend(Backends backends, IHttpContextAccessor accessor)
{
    public Backend Backend { get; } = backends.For(accessor.HttpContext);

    public InventoryService Inventory => Backend.Inventory;

    public BidService Bids => Backend.Bids;

    public MarketService Market => Backend.Market;

    /// <summary>The three an endpoint used to take one by one, in the order it took them.</summary>
    public void Deconstruct(out InventoryService inventory, out BidService bids, out MarketService market)
    {
        inventory = Inventory;
        bids = Bids;
        market = Market;
    }
}

// #region warm-before-reading
/// <summary>
/// The request's store, warm before any endpoint reads it.
///
/// <para>The host awaits the default store's catalogue before it serves and
/// warms the others in the background on a deployed group, so a request that
/// reaches a store between those two moments, a visitor following the toggle
/// in the seconds after a roll, or any request to the other store in a test
/// application where the background warm is off, found a catalogue still
/// loading. <c>InventoryService</c>'s synchronous accessors then blocked the
/// request thread on the load, on a container with one vCPU and therefore one
/// thread pool worker to start with: the exact shape ADR: The ports learn to
/// wait argued against and said the host did not do. Three readers with no
/// memory of the project read the accessor and asked; it was the one path
/// the record's claim did not cover.</para>
///
/// <para>So the pipeline awaits the warm, once per request, before the
/// endpoint runs. After the first time it is an await on a task that finished
/// at startup, which is no wait at all. The bid replay is awaited by the
/// writing methods already and read by the reads as whatever has loaded, so
/// it is not gated here; a listing during the seconds a store's bids replay
/// shows the dataset's prices, the same as before.</para>
/// </summary>
public static class Warmth
{
    public static Task EnsureAsync(Backend backend) => EnsureAsync(backend, DateTimeOffset.UtcNow);

    /// <summary>
    /// The touch comes first, on purpose: a catalogue that has just been
    /// touched cannot be let go, and one that was let go a moment ago is cold
    /// here and gets loaded again, awaited (the let-go region of InventoryService).
    /// </summary>
    public static Task EnsureAsync(Backend backend, DateTimeOffset now)
    {
        backend.Inventory.Touch(now);
        return backend.Inventory.IsWarm ? Task.CompletedTask : backend.Inventory.WarmAsync();
    }
}
// #endregion warm-before-reading

// #region catalogue-keeper
/// <summary>
/// Gives back the catalogue of a store this site does not serve, once nobody
/// has asked that store for anything in a while (ADR: One plan, two sites).
///
/// <para>Each site serves one store and can answer for the other: the proof
/// card drives both, and a measurement can name either with a header. The
/// first such request loads the other store's hundred thousand vehicles, and
/// until this existed they stayed loaded until the next roll. On a container
/// group with 1.5 GB to itself that was free. On a plan two sites share it
/// was measured as the difference between fitting and paging: about 130 MB
/// of managed heap a site, twice, held for a card somebody pressed once.</para>
///
/// <para>The default store is never let go: it is what the site is. The idle
/// time is configuration (<c>Store:ReleaseIdleMinutes</c>), and zero, which
/// is the default, means never, so a developer's machine and the test suite
/// behave exactly as they did. After a release the collector is asked for a
/// full compacting collection, once: that is the difference between memory
/// the runtime could reuse and memory the machine gets back, and the machine
/// getting it back is the point.</para>
/// </summary>
public sealed class CatalogueKeeper(Backends backends, TimeSpan idle, ILogger<CatalogueKeeper> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>The stores whose catalogues were let go on this pass, by key. Public so a test can drive a pass with its own clock.</summary>
    public IReadOnlyList<string> Sweep(DateTimeOffset now)
    {
        if (idle <= TimeSpan.Zero)
        {
            return [];
        }

        var released = new List<string>();
        foreach (var backend in backends.All)
        {
            if (!ReferenceEquals(backend, backends.Default) && backend.Inventory.ReleaseIfIdle(idle, now))
            {
                released.Add(backend.Key);
            }
        }

        return released;
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (idle <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stopping))
        {
            var released = Sweep(DateTimeOffset.UtcNow);
            if (released.Count == 0)
            {
                continue;
            }

            logger.LogInformation(
                "Let go of the {Stores} catalogue after {Minutes} idle minutes; its next request loads it again",
                string.Join(", ", released),
                (int)idle.TotalMinutes);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
    }
}
// #endregion catalogue-keeper

/// <summary>
/// A context per call, from options fixed at startup. The relational backend
/// is built before the container is, so it cannot take the factory the
/// container would have registered; this is the same thing with no pool and
/// no service provider behind it.
///
/// <para>One thing the container's factory did for free has to be done by
/// hand: Entity Framework logs every command through the application's logger
/// factory, which is what puts its `@p='?'` lines in the Admin tab's log
/// section, and that factory does not exist until the application is built.
/// <see cref="Attach"/> hands it over then, before the catalogue is read, so
/// the first statement is logged like the last.</para>
/// </summary>
public sealed class ContextFactory(DbContextOptions<YardDbContext> options) : IDbContextFactory<YardDbContext>
{
    private DbContextOptions<YardDbContext> _options = options;

    public YardDbContext CreateDbContext() => new(_options);

    /// <summary>The application's logging, once there is an application.</summary>
    public void Attach(ILoggerFactory loggers) =>
        _options = new DbContextOptionsBuilder<YardDbContext>(_options).UseLoggerFactory(loggers).Options;
}

/// <summary>One store on the toggle: its key, its name, whether it came up, and whether it is the container's default.</summary>
public sealed record StoreView(string Key, string Name, bool Ready, bool Default);

/// <summary>The bar's answer: which store this request is on, the stores there are (one of them this site's default), and the other site if there is one.</summary>
public sealed record StoresView(string Current, IReadOnlyList<StoreView> Stores, string? OtherSite);
// #endregion backends
