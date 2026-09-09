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

    public required DatabaseState Database { get; init; }

    public required StartupTimings Startup { get; init; }

    public required InventoryService Inventory { get; init; }

    public required BidService Bids { get; init; }

    public required MarketService Market { get; init; }

    /// <summary>The document store, on the Cosmos DB backend only.</summary>
    public CosmosStore? Cosmos { get; init; }

    /// <summary>The relational contexts, on the SQL backend only, and only when it came up.</summary>
    public IDbContextFactory<YardDbContext>? Contexts { get; init; }

    /// <summary>Is the seed catalogue in the store right now. Two reads, timed by the health check.</summary>
    public required Func<Task<bool>> Probe { get; init; }

    /// <summary>
    /// Identity's store over this backend's accounts, built per request from
    /// the request's scope, or null when the store did not come up and there
    /// are no accounts to keep.
    /// </summary>
    public required Func<IServiceProvider, IUserStore<YardUser>?> UserStore { get; init; }

    /// <summary>
    /// Whether this backend keeps accounts and bids across a restart. False is
    /// the fallback the relational store record describes: the catalogue is
    /// served from files, the bidding works, and nothing outlives the process.
    /// </summary>
    public bool Ready => Database.Ready;
}
// #endregion backend

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
    public static Task EnsureAsync(Backend backend) =>
        backend.Inventory.IsWarm ? Task.CompletedTask : backend.Inventory.WarmAsync();
}
// #endregion warm-before-reading

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
