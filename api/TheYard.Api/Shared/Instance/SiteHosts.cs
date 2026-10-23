// The public names this one process answers to, and which of them a request came in on
// (ADR: One web app for every .NET process). Read by Stores.cs for the store a request gets
// when it names none, and by Shared/ShedMount.cs for the requests that belong to a guest site.
namespace TheYard.Api.Shared.Instance;

// #region site-hosts
/// <summary>
/// The public names one web app serves, as a configured list (ADR: The onion cut into vertical
/// slices, rule 3): each site is a name and, for a Yard site, the key of the store it serves; a
/// site with no store is a guest application this process hosts, The Shed today. The list is read
/// from configuration (SiteSettings.cs), so a third site or a third store is a setting, and nothing
/// here names a store or a host.
///
/// <para>Which name a request arrived on is the Host header when App Service routed it here by
/// that name, which is how a guest's domain arrives, bound straight to the web app. The Yard domains
/// arrive through the edge, which proxies to the web app's own address and says which domain the
/// visitor typed in <see cref="HeaderName"/> (netlify.toml); the header is read only when the Host
/// is not one of the configured names. A header is what the sender says it is, so it counts only
/// when it names one of them: the most a sender can choose is which configured site answers, the
/// same choice typing the other address makes.</para>
/// </summary>
public sealed class SiteHosts
{
    /// <summary>The header the edge writes with the domain the visitor typed.</summary>
    public const string HeaderName = "X-Yard-Host";

    private readonly Site[] _sites;

    /// <summary>The configured address for links when a request arrived on no Yard name (Site:Url), without its trailing slash; null when none is set.</summary>
    private readonly string? _siteUrl;

    /// <summary>The sites, each name cleaned to lower case; a site whose name is empty or a placeholder is left out.</summary>
    /// <param name="sites">The configured sites, in order.</param>
    /// <param name="siteUrl">Site:Url, the address a link falls back to; a placeholder or an empty value is none.</param>
    public SiteHosts(IEnumerable<Site> sites, string? siteUrl = null)
    {
        _siteUrl = string.IsNullOrWhiteSpace(siteUrl) || siteUrl.StartsWith("__", StringComparison.Ordinal) ? null : siteUrl.Trim().TrimEnd('/');
        _sites = [.. sites
            .Select(site => site with { Host = Clean(site.Host) ?? "", Store = string.IsNullOrWhiteSpace(site.Store) ? null : site.Store.Trim() })
            .Where(site => site.Host.Length > 0)];
    }

    /// <summary>No names at all: a developer's machine or the test host, where the store comes from Store:Default.</summary>
    public static SiteHosts None { get; } = new([]);

    /// <summary>Every configured site, in order.</summary>
    public IReadOnlyList<Site> All => _sites;

    /// <summary>The names that serve a store, the Yard's own sites.</summary>
    public IEnumerable<Site> StoreSites => _sites.Where(site => site.Store is not null);

    /// <summary>The names that serve no store: guest applications this process hosts.</summary>
    public IEnumerable<string> Guests => _sites.Where(site => site.Store is null).Select(site => site.Host);

    /// <summary>
    /// The configured name this request arrived on, or null when it arrived on none of them
    /// (the web app's own azurewebsites.net address with no header, a health probe, a test).
    /// </summary>
    /// <param name="context">The request, or null outside one.</param>
    public string? Of(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }
        // The Host header first: a name App Service routed here by is the name, whatever a
        // header says. Only a request on another name (the web app's own address, which is
        // where the edge sends the Yard names) is told its name by the edge's header.
        if (Known(context.Request.Host.Host) is { } routed)
        {
            return routed.Host;
        }
        return context.Request.Headers.TryGetValue(HeaderName, out var said) ? Known(said.ToString())?.Host : null;
    }

    /// <summary>The key of the store a name serves when the request names none, or null for a guest's name or any other.</summary>
    /// <param name="host">A name, already one of the configured ones or null.</param>
    public string? StoreFor(string? host) => Array.Find(_sites, site => site.Host == host)?.Store;

    /// <summary>The public name of the site whose store is <paramref name="key"/>, or null when that store has no site here.</summary>
    /// <param name="key">A store's key.</param>
    public string? HostFor(string key) =>
        Array.Find(_sites, site => string.Equals(site.Store, key, StringComparison.OrdinalIgnoreCase))?.Host;

    /// <summary>The public origin of the site whose store is <paramref name="key"/>, or null when that store has no site here.</summary>
    /// <param name="key">A store's key.</param>
    public string? OriginOf(string key) => HostFor(key) is { } host ? $"https://{host}" : null;

    /// <summary>
    /// The Yard site this request arrived on, as an origin for a link (a reset email, the About
    /// page), or null when it arrived on no Yard name and the caller falls back to Site:Url.
    /// </summary>
    /// <param name="context">The request, or null outside one.</param>
    public string? SiteOrigin(HttpContext? context) =>
        Of(context) is { } host && StoreFor(host) is not null ? $"https://{host}" : null;

    /// <summary>
    /// The origin a link sent from this request carries (a reset email, the About page): the Yard
    /// site the request arrived on, then the configured Site:Url, then the request's own scheme and
    /// host on a machine with neither. Never X-Forwarded-Host: a link must not be one a sender chose.
    /// </summary>
    /// <param name="context">The request.</param>
    public string LinkOrigin(HttpContext context) =>
        SiteOrigin(context) ?? _siteUrl ?? $"{context.Request.Scheme}://{context.Request.Host.Value}";

    /// <summary>The guest site this request arrived on, or null when it arrived on a Yard name or none.</summary>
    /// <param name="context">The request.</param>
    public string? GuestOf(HttpContext context) =>
        Of(context) is { } host && StoreFor(host) is null ? host : null;

    private Site? Known(string? host)
    {
        string? clean = Clean(host);
        return clean is null ? null : Array.Find(_sites, site => site.Host == clean);
    }

    // A deploy's unreplaced placeholder ("__HOSTS_SQL__") reads as no name, as Peer:Url's does.
    private static string? Clean(string? host) =>
        string.IsNullOrWhiteSpace(host) || host.StartsWith("__", StringComparison.Ordinal)
            ? null
            : host.Trim().TrimEnd('.').ToLowerInvariant();
}

/// <summary>One public name this process answers to, and the store it serves.</summary>
/// <param name="Host">The name, lower case.</param>
/// <param name="Store">The key of the store the name serves when a request names none, or null for a guest application's name.</param>
public sealed record Site(string Host, string? Store);
// #endregion site-hosts
