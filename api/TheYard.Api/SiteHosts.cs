// The public names this one process answers to, and which of them a request came in on
// (ADR: One web app for every .NET process). Read by Stores.cs for the store a request gets
// when it names none, and by Composition/ShedMount.cs for the requests that belong to the Shed.
namespace TheYard.Api;

// #region site-hosts
/// <summary>
/// The three public names one web app serves since 19 October: the SQL site, the Cosmos DB
/// site and The Shed (Hosts:Sql, Hosts:Cosmos and Hosts:Shed). Until then each was a web app
/// of its own, and a site's store was a setting on its container (Store:Default); now the
/// store a request gets when it names none follows the name it arrived on.
///
/// <para>Which name a request arrived on is the Host header when App Service routed it here
/// by that name, which is how The Shed's domain arrives, bound straight to the web app. The
/// two Yard domains arrive through the edge, which proxies to the web app's own address and
/// says which domain the visitor typed in <see cref="HeaderName"/> (netlify.toml); the header
/// is read only when the Host is not one of the three. A header is what the sender says it is,
/// so it counts only when it names one of the three names configured here: the most a sender can choose is which of these three sites answers, the
/// same choice typing the other address makes.</para>
/// </summary>
public sealed class SiteHosts
{
    /// <summary>The header the edge writes with the domain the visitor typed.</summary>
    public const string HeaderName = "X-Yard-Host";

    /// <summary>The names, read from configuration, each lower case, or null where none is set.</summary>
    public SiteHosts(string? sql, string? cosmos, string? shed)
    {
        Sql = Clean(sql);
        Cosmos = Clean(cosmos);
        Shed = Clean(shed);
    }

    /// <summary>The SQL site's public name, or null.</summary>
    public string? Sql { get; }

    /// <summary>The Cosmos DB site's public name, or null.</summary>
    public string? Cosmos { get; }

    /// <summary>The Shed's public name, or null.</summary>
    public string? Shed { get; }

    /// <summary>No names at all: a developer's machine or the test host, where the store comes from Store:Default as before.</summary>
    public static SiteHosts None { get; } = new(null, null, null);

    /// <summary>The names from the Hosts section of configuration.</summary>
    public static SiteHosts From(IConfiguration configuration) =>
        new(configuration["Hosts:Sql"], configuration["Hosts:Cosmos"], configuration["Hosts:Shed"]);

    /// <summary>
    /// The configured name this request arrived on, or null when it arrived on none of them
    /// (the web app's own azurewebsites.net address with no header, a health probe, a test).
    /// </summary>
    public string? Of(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }
        // The Host header first: a name App Service routed here by is the name, whatever a
        // header says. Only a request on another name (the web app's own address, which is
        // where the edge sends both Yard names) is told its name by the edge's header.
        if (Known(context.Request.Host.Host) is { } routed)
        {
            return routed;
        }
        return context.Request.Headers.TryGetValue(HeaderName, out var said) ? Known(said.ToString()) : null;
    }

    /// <summary>The store a name serves when the request names none: "sql", "cosmos", or null for any other name.</summary>
    public string? StoreFor(string? host) =>
        host is null ? null
        : host == Cosmos ? "cosmos"
        : host == Sql ? "sql"
        : null;

    /// <summary>The public origin of the site whose store is <paramref name="key"/>, or null when that site has no name here.</summary>
    public string? OriginOf(string key) =>
        (string.Equals(key, "cosmos", StringComparison.OrdinalIgnoreCase) ? Cosmos
        : string.Equals(key, "sql", StringComparison.OrdinalIgnoreCase) ? Sql
        : null) is { } host ? $"https://{host}" : null;

    /// <summary>
    /// The site this request arrived on, as an origin for a link (a reset email, the About
    /// page), or null when it arrived on no Yard name and the caller falls back to Site:Url.
    /// </summary>
    public string? SiteOrigin(HttpContext? context) =>
        Of(context) is { } host && (host == Sql || host == Cosmos) ? $"https://{host}" : null;

    /// <summary>True when the request arrived on The Shed's name.</summary>
    public bool IsShed(HttpContext context) => Shed is not null && Of(context) == Shed;

    private string? Known(string? host)
    {
        string? clean = Clean(host);
        return clean is not null && (clean == Sql || clean == Cosmos || clean == Shed) ? clean : null;
    }

    // A deploy's unreplaced placeholder ("__HOSTS_SQL__") reads as no name, as Peer:Url's does.
    private static string? Clean(string? host) =>
        string.IsNullOrWhiteSpace(host) || host.StartsWith("__", StringComparison.Ordinal)
            ? null
            : host.Trim().TrimEnd('.').ToLowerInvariant();
}
// #endregion site-hosts
