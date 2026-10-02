// The stores this container runs, and which one a request gets: the backends, the request's
// choice, the warm before a read, the keeper that lets an idle catalogue go, and the context
// factory. The rest of the store wiring lives in files beside this one:
//   Backend.cs                        one store and what stands on it (Backend, StoreAttachment)
//   StorePrepare.cs                   the five asks a store gets at startup
//   StoreSecondChance.cs              the store tried again after startup
//   Composition/StoreRegistration.cs  where the stores are brought up and registered
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Api;

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
/// <para>An older toggle chose the store with a cookie, which a browser may
/// still carry. It no longer chooses anything, and the stores endpoint
/// expires it on sight, so the browser lands where the address bar says
/// rather than on a store the page can no longer switch away from.</para>
/// </summary>
public sealed class Backends
{
    /// <summary>The cookie the toggle used to set. Read only to be expired.</summary>
    public const string LegacyCookieName = "yard-store";

    /// <summary>The request header that names a store: "sql" or "cosmos".</summary>
    public const string HeaderName = "X-Yard-Store";

    /// <summary>Every store this container runs, in the order they were brought up.</summary>
    private readonly Backend[] _all;

    /// <summary>The stores, the key of the one a request gets when it names none, and the other site's address if there is one.</summary>
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

    /// <summary>Every store this container runs.</summary>
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

    /// <summary>The store with this key, ignoring case, or null when there is none.</summary>
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
    /// browser that used the old toggle. Nothing else reads it.
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
    /// <summary>The store this request named, or the container's default.</summary>
    public Backend Backend { get; } = backends.For(accessor.HttpContext);

    /// <summary>The request's catalogue.</summary>
    public InventoryService Inventory => Backend.Inventory;

    /// <summary>The request's bids.</summary>
    public BidService Bids => Backend.Bids;

    /// <summary>The request's competing bidders.</summary>
    public MarketService Market => Backend.Market;

    /// <summary>The catalogue, the bids and the competing bidders in one line, for an endpoint that wants all three.</summary>
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
/// wait argued against (ADR: Three readers with no memory of the project).</para>
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
    /// <summary>Warms the backend's catalogue if it is not warm, on the real clock.</summary>
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
/// without this they would stay loaded until the next roll. On a container
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
    /// <summary>How often the keeper looks for an idle catalogue.</summary>
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

    /// <summary>Sweeps once a minute and, after any release, asks for a full compacting collection; does nothing when the idle time is zero.</summary>
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
    /// <summary>The options every context is built from; replaced once when the loggers arrive.</summary>
    private DbContextOptions<YardDbContext> _options = options;

    /// <summary>A new context for one operation; the caller disposes it.</summary>
    public YardDbContext CreateDbContext() => new(_options);

    /// <summary>The application's logging, once there is an application.</summary>
    public void Attach(ILoggerFactory loggers) =>
        _options = new DbContextOptionsBuilder<YardDbContext>(_options).UseLoggerFactory(loggers).Options;
}

/// <summary>One store on the toggle: its key, its name, whether it came up, and whether it is the container's default.</summary>
/// <param name="Key">The store's key.</param>
/// <param name="Name">The store's display name.</param>
/// <param name="Ready">True when the store came up.</param>
/// <param name="Default">True when it is the store a request gets when it names none.</param>
public sealed record StoreView(string Key, string Name, bool Ready, bool Default);

/// <summary>The bar's answer: which store this request is on, the stores there are (one of them this site's default), and the other site if there is one.</summary>
/// <param name="Current">The key of the store this request is on.</param>
/// <param name="Stores">Every store on the toggle.</param>
/// <param name="OtherSite">The other site's http or https origin, or null when there is none.</param>
public sealed record StoresView(string Current, IReadOnlyList<StoreView> Stores, string? OtherSite);
// #endregion backends
