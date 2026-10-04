// The stores this container runs, and which one a request gets: the backends, the request's
// choice and the warm before a read. The rest of the store wiring lives in files beside this
// one, and the relational context factory lives with the
// relational adapters in TheYard.Infrastructure/ContextFactory.cs:
//   Backend.cs                        one store and what stands on it (Backend, StoreAttachment)
//   StorePrepare.cs                   the five asks a store gets at startup
//   StoreSecondChance.cs              the store tried again after startup
//   Composition/StoreRegistration.cs  where the stores are brought up and registered
using TheYard.Application;

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
/// <para>A cookie named yard-store chooses nothing. A browser may still carry
/// one, so the stores endpoint expires it on sight, and the browser lands
/// where the address bar says.</para>
/// </summary>
public sealed class Backends
{
    /// <summary>A cookie that chooses nothing and is read only to be expired.</summary>
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

    /// <summary>
    /// The request's auction: the catalogue, the bids and the room composed by the Application
    /// ring. A new one per read is three references and nothing more, and it always sees the
    /// services the backend holds right now, including after the store is attached. The services
    /// themselves are not offered here, so an endpoint can only reach them through the auction.
    /// </summary>
    public Auction Auction => new(Backend.Inventory, Backend.Bids, Backend.Market);
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
/// shows the dataset's prices until the replay lands.</para>
/// </summary>
public static class Warmth
{
    /// <summary>Warms the backend's catalogue if it is not warm; a warm one is a task already finished.</summary>
    public static Task EnsureAsync(Backend backend) =>
        backend.Inventory.IsWarm ? Task.CompletedTask : backend.Inventory.WarmAsync();
}
// #endregion warm-before-reading

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
