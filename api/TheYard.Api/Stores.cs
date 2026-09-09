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
    /// <summary>The short name a cookie or a header uses: "sql" or "cosmos".</summary>
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
/// <para>The choice is a cookie, set by the toggle at the top of the page, or
/// a header, which is how a measurement asks for a store without a browser.
/// Neither names a store this container does not have: an unknown value is
/// the default, never an error, because a cookie set by last week's build is
/// not the visitor's mistake.</para>
/// </summary>
public sealed class Backends
{
    public const string CookieName = "yard-store";
    public const string HeaderName = "X-Yard-Store";

    private readonly Backend[] _all;

    public Backends(IEnumerable<Backend> all, string? defaultKey)
    {
        _all = all.ToArray();
        if (_all.Length == 0)
        {
            throw new ArgumentException("a container needs at least one store", nameof(all));
        }

        Default = Named(defaultKey) ?? _all[0];
    }

    public IReadOnlyList<Backend> All => _all;

    /// <summary>The backend a request gets when it names none.</summary>
    public Backend Default { get; }

    public Backend? Named(string? key) =>
        key is null ? null : Array.Find(_all, backend => string.Equals(backend.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The backend a request asked for: the header first, then the cookie, then the default.</summary>
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
        if (context.Request.Cookies.TryGetValue(CookieName, out string? cookie) && Named(cookie) is { } byCookie)
        {
            return byCookie;
        }
        return Default;
    }

    /// <summary>
    /// The toggle's choice: a store this container has, and one that came up.
    /// A store that did not come up still serves the catalogue from files, but
    /// it has no accounts and no bids to switch to, so choosing it would set a
    /// year-long cookie for a store with nothing behind it; the bar does not
    /// offer it and the server does not take it either, so a stale page or a
    /// script gets the same answer a visitor would. The refusal is a sentence
    /// the page can show.
    /// </summary>
    public (Backend? Backend, string? Refusal) Choose(string? key)
    {
        if (Named(key) is not { } chosen)
        {
            return (null, $"This container runs {string.Join(" and ", _all.Select(backend => backend.Name))}, and nothing called \"{key}\".");
        }
        if (!chosen.Ready)
        {
            return (null, $"{chosen.Name} did not come up on this container, so there are no accounts or bids to switch to; the catalogue it would serve is the same one.");
        }
        return (chosen, null);
    }

    /// <summary>The cookie the toggle sets: a year, the whole site, and never readable by a script, which has no reason to read it.</summary>
    public static CookieOptions CookieFor(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps
            || string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase),
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromDays(365),
    };

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
                ReferenceEquals(backend, Default))).ToArray());
    }
}

/// <summary>
/// The backend serving this request, resolved once per request from the
/// request's own headers and cookies. Endpoints ask this rather than the
/// container, so the same handler serves whichever store the visitor chose.
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

/// <summary>The toggle's answer: which store this request is on, and the stores there are.</summary>
public sealed record StoresView(string Current, IReadOnlyList<StoreView> Stores);

/// <summary>The toggle's request: the key of the store to switch to.</summary>
public sealed record StoreChoice(string? Store);
// #endregion backends
