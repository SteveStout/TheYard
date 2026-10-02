// What a request is recorded as, and what is left out: which requests count,
// the guess at a bot, the site reading itself, and the referring host. Its own
// file because it is the rule a reader checks first when asking what a row can
// hold; Activity.cs lists the other parts of the activity feature.
using System.Text.RegularExpressions;
using TheYard.Application;

namespace TheYard.Api;

// #region hits
/// <summary>
/// What is recorded about a request, and what is not. The path, cut at the
/// query string and with any at sign encoded, so an address pasted into a
/// URL cannot travel; the store that served it; a guess at whether it was a
/// person. Photos and the page's own assets are not recorded, because one
/// visit is one page and not the forty files it is made of, and the Admin
/// tab's own reads are excluded by the caller for the reason the request ring
/// gives.
/// </summary>
public static class Hits
{
    /// <summary>User agents that name a crawler, a script or a scanning tool, matched anywhere in the agent and ignoring case.</summary>
    private static readonly Regex BotAgent = new(
        @"bot|crawl|spider|slurp|curl|wget|python|httpclient|go-http|java/|libwww|scan|nmap|masscan|zgrab|censys|nuclei|headless|phantom",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Paths only a scanner asks for: PHP pages, WordPress, secrets files, admin tools this site never had.</summary>
    private static readonly Regex BotPath = new(
        @"\.php$|/wp-|/\.env|/\.git|/xmlrpc|/phpmyadmin|/cgi-bin|/vendor/|/\.aws|/config\.|/\.well-known/security",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The file extensions of the page's own assets, which are not counted as visits.</summary>
    private static readonly HashSet<string> Assets = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".webp", ".gif",
        ".woff", ".woff2", ".ttf", ".txt", ".xml", ".json", ".webmanifest",
    };

    /// <summary>Is this request one the activity feature counts.</summary>
    public static bool Counts(string path)
    {
        if (path.StartsWith("/api/images/", StringComparison.Ordinal))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Length == 0 || !Assets.Contains(extension);
    }

    /// <summary>
    /// The guess at whether a request was a bot: no user agent at all, an agent
    /// that names a tool, or a path only a scanner asks for. A guess, because
    /// the agent is whatever the sender says it is.
    /// </summary>
    public static bool LooksLikeABot(string? userAgent, string path) =>
        string.IsNullOrWhiteSpace(userAgent) || BotAgent.IsMatch(userAgent) || BotPath.IsMatch(path);

    /// <summary>
    /// The mark every tool that reads this site on its operator's behalf
    /// carries on its user agent: the page sweep, the ship's readers, the
    /// card's own pictures. One string, so one rule finds them all.
    /// </summary>
    public const string SelfTag = "TheYard-SelfRead";

    /// <summary>
    /// The network a request from the site's own tools is kept under, in
    /// place of its three octets: the card counts it as the site reading
    /// itself, and the row says so rather than naming the operator's network.
    /// It ends in x like every other network.
    /// </summary>
    public const string SelfNetwork = "self:x";

    /// <summary>The user agents App Service sends when it keeps the container warm or asks whether it is up.</summary>
    private static readonly Regex PlatformAgent = new(
        @"^AlwaysOn|HealthCheck|ReadyForRequest",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Whether a request is the site reading itself: one of its own tools, or
    /// App Service keeping the container warm or asking whether it is up.
    /// The user agent is what the sender says it is, so a stranger can hide
    /// from the card by claiming the mark; that costs a count of visitors,
    /// never anything a request can reach.
    /// </summary>
    public static bool IsSelf(string? userAgent) =>
        !string.IsNullOrWhiteSpace(userAgent)
        && (userAgent.Contains(SelfTag, StringComparison.OrdinalIgnoreCase) || PlatformAgent.IsMatch(userAgent));

    /// <summary>
    /// Whether a kept network is the site's own: the mark above, or the
    /// loopback address, which is this machine talking to itself by
    /// definition. Read at report time, so older rows, written when App
    /// Service's own requests were still counted as people, read the same way
    /// as the rows written now.
    /// </summary>
    public static bool IsSelfNetwork(string network) =>
        network == SelfNetwork
        || network.StartsWith("127.", StringComparison.Ordinal)
        || network.StartsWith("::1:", StringComparison.Ordinal)
        || network.StartsWith("::ffff:127.", StringComparison.Ordinal);

    /// <summary>Is this network the loopback address, the one machine every such row is.</summary>
    public static bool IsLoopbackNetwork(string network) => network != SelfNetwork && IsSelfNetwork(network);

    /// <summary>
    /// The hit a request becomes. The token and the network are the ones the
    /// caller made for the kept log. A read by the site's own tools is kept
    /// under a token of its own, the keyed hash of the mark and the address,
    /// so it is its own row and never folds into the row of a person at the
    /// same address (a row keeps the network of its first hit); its network
    /// is the mark, and it is flagged as not a person, so an hour's human
    /// count leaves it out as well. That is the one request that pays for a
    /// second keyed hash.
    /// </summary>
    public static ActivityHit For(VisitorTokens tokens, string address, DateTimeOffset at, string token, string network, string path, string store, string? userAgent, string? source = null) =>
        IsSelf(userAgent)
            ? new ActivityHit(at, tokens.TokenFor(SelfNetwork + "|" + address, at), SelfNetwork, PathOf(path), store, true)
            : new ActivityHit(at, token, network, PathOf(path), store, LooksLikeABot(userAgent, path), source);

    /// <summary>What a page load with no referring page is kept under: typed, bookmarked, or sent by something that says nothing.</summary>
    public const string NoSource = "(none)";

    /// <summary>The shape a referring host must have to be kept by name: lowercase letters, digits, dots and hyphens, at most a hundred.</summary>
    private static readonly Regex HostShape = new(@"^[a-z0-9.-]{1,100}$", RegexOptions.Compiled);

    /// <summary>
    /// Where a page load came from: the host of the Referer, lowercased,
    /// and nothing else of it, so a path or a query that could carry something
    /// about the visitor never reaches a row. Only the page itself is asked,
    /// because every call the page makes after it names this site as its
    /// referrer. A referrer that is this site, the other store's site or the
    /// App Service origin is moving within the site and is no source; no
    /// referrer at all is kept as "(none)", typed or unknown; anything that is
    /// not a web address's host is kept as "other".
    /// </summary>
    public static string? SourceOf(string? referer, string path, string requestHost)
    {
        if (path != "/" && path != "/index.html")
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(referer))
        {
            return NoSource;
        }

        if (!Uri.TryCreate(referer.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "other";
        }

        string host = uri.Host.ToLowerInvariant();
        if (string.Equals(host, requestHost, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".azurewebsites.net", StringComparison.Ordinal)
            || (host.StartsWith("theyard", StringComparison.Ordinal) && host.EndsWith(".stevenstout.biz", StringComparison.Ordinal)))
        {
            return null;
        }

        return HostShape.IsMatch(host) ? host : "other";
    }

    /// <summary>The path a row keeps: no query string, bounded, and with no at sign in it.</summary>
    public static string PathOf(string path)
    {
        string kept = path.Length > 200 ? path[..200] : path;
        return kept.Replace("@", "%40", StringComparison.Ordinal);
    }
}
// #endregion hits
