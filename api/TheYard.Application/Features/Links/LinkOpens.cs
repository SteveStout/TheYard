// Tagged links (ADR: Tagged links, who opened what): a link the operator sends out carries an
// opaque six-character code, the site records that the code was opened and on which path, and
// the code alone ever reaches a row. Who the code was sent to lives off the site, in the operator's
// own lookup; the Admin tab shows codes, never names. This file is the whole rule: what a code
// is, what an open carries, how opens fold into one row a code a day, and the port they are kept
// through.
using System.Text.RegularExpressions;

namespace TheYard.Application.Features.Links;

// #region link-codes
/// <summary>What a code is: six lower-case letters or digits, the shape make_ref.py mints and nothing a person could be named by.</summary>
public static partial class LinkCodes
{
    /// <summary>The query key a tagged link carries the code under.</summary>
    public const string QueryKey = "r";

    /// <summary>True for a string that is a code.</summary>
    /// <param name="value">The query value, or null.</param>
    public static bool IsOne(string? value) => value is not null && Shape().IsMatch(value);

    [GeneratedRegex("^[a-z0-9]{6}$")]
    private static partial Regex Shape();
}
// #endregion link-codes

// #region link-previews
/// <summary>
/// The fetchers that open a link before a person does: the preview an email client, a chat tool
/// or a social site draws under a pasted link, and the scanners a mail gateway sends first. An
/// open by one of these is not the person reading, so it is not counted. A guess by the agent
/// string, like every bot rule here; a scanner that borrows a browser's agent is counted as a
/// person, and the report says "opened" where it should say "scanned", which is the cheaper
/// mistake.
/// </summary>
public static partial class LinkPreviews
{
    /// <summary>
    /// True when the agent names a preview fetcher or a link scanner, or there is no agent at all:
    /// a request with no agent is a tool, never a person's browser. "bot" counts only at the end of
    /// a word (Googlebot/, Slackbot-), so a phone named CUBOT_X30 is a person.
    /// </summary>
    /// <param name="userAgent">The request's User-Agent header, or null.</param>
    public static bool IsOne(string? userAgent) => string.IsNullOrWhiteSpace(userAgent) || Fetcher().IsMatch(userAgent);

    [GeneratedRegex(
        "LinkedInBot|Slackbot|Slack-ImgProxy|facebookexternalhit|Facebot|Twitterbot|WhatsApp|TelegramBot|Discordbot|Google-PageRenderer|GoogleImageProxy|Google-Safety|"
        + "SkypeUriPreview|Iframely|Embedly|Bitly|Zoombot|ms-office|MSOffice|Microsoft Office|OfficeProtocolDiscovery|SafeLinks|Outlook-iOS|BingPreview|Yahoo Link Preview|"
        + "Mimecast|Proofpoint|Barracuda|ClamAV|Symantec|Mailchimp|HubSpot|Apple-PubSub|AppleMail|Applebot|"
        + "Mastodon|Cardyb|axios|okhttp|node-fetch|Dalvik|"
        + @"bot\b|crawl|spider|slurp|curl|wget|python|httpclient|go-http|java/|libwww|scan|headless|phantom|TheYard-SelfRead",
        RegexOptions.IgnoreCase)]
    private static partial Regex Fetcher();
}
// #endregion link-previews

// #region tagged-requests
/// <summary>
/// Which requests count as an open, decided once for the pipeline and the page's own report:
/// a GET whose query carries a code, not from a preview fetcher or a scanner, that the site
/// answered: a 404 on /api/go/nope is a guess, not a link anyone sent. The page's report
/// (/api/links/open) counts under the view the page opened on, because the page itself is served
/// elsewhere; every other tagged request counts under its own path with the query cut off.
/// </summary>
public static class TaggedRequests
{
    /// <summary>The path the page reports its own open on.</summary>
    public const string PageReport = "/api/links/open";

    /// <summary>The views the page may report: the home page and About Steven.</summary>
    public static readonly IReadOnlyList<string> PageViews = ["/", "/about"];

    /// <summary>The paths a sent link may name beside the page's views: the resume behind its door and the pass-throughs.</summary>
    public static readonly IReadOnlyList<string> SentPrefixes = ["/resume", "/api/go/"];

    /// <summary>Whether a path is one a sent link names, so a code on any other request (a cache-buster, a guess) is not an open.</summary>
    /// <param name="path">The request's path, with no query.</param>
    public static bool IsSent(string path) => PageViews.Contains(path, StringComparer.Ordinal)
        || SentPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>The open this request is, or null when it is not one.</summary>
    /// <param name="method">The request's method.</param>
    /// <param name="path">The request's path, with no query.</param>
    /// <param name="code">The query's r value, or null.</param>
    /// <param name="pagePath">The query's path value on the page's report, or null.</param>
    /// <param name="userAgent">The User-Agent header, or null.</param>
    /// <param name="at">When the request arrived.</param>
    /// <param name="store">The store serving it.</param>
    /// <param name="status">The status the site answered with; 400 and above is never an open.</param>
    public static LinkOpen? OpenOf(string method, string path, string? code, string? pagePath, string? userAgent, DateTimeOffset at, string store, int status = 200)
    {
        if (status >= 400 || !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || !LinkCodes.IsOne(code) || LinkPreviews.IsOne(userAgent))
        {
            return null;
        }
        if (string.Equals(path, PageReport, StringComparison.Ordinal))
        {
            return pagePath is not null && PageViews.Contains(pagePath, StringComparer.Ordinal) ? new LinkOpen(at, code!, pagePath, store) : null;
        }
        return IsSent(path) ? new LinkOpen(at, code!, path, store) : null;
    }
}
// #endregion tagged-requests

// #region link-open
/// <summary>One open of a tagged link, as the site keeps it: when, the code, the path with no query, and the store that served it.</summary>
/// <param name="At">When the request arrived.</param>
/// <param name="Code">The six-character code the link carried.</param>
/// <param name="Path">The path requested, with no query string.</param>
/// <param name="Store">The store that served the request.</param>
public sealed record LinkOpen(DateTimeOffset At, string Code, string Path, string Store);

/// <summary>One code's opens on one UTC day, folded: first and last, the count, and the paths with a count each.</summary>
/// <param name="Day">The UTC day as yyyy-MM-dd.</param>
/// <param name="Code">The code.</param>
/// <param name="First">The first open that day.</param>
/// <param name="Last">The last open that day.</param>
/// <param name="Opens">How many opens that day.</param>
/// <param name="Paths">The paths opened, with a count each.</param>
public sealed record LinkOpenDay(string Day, string Code, DateTimeOffset First, DateTimeOffset Last, int Opens, IReadOnlyDictionary<string, int> Paths);

/// <summary>Whether opens can be kept and read, and why not when they cannot.</summary>
/// <param name="Available">True when the store keeps opens.</param>
/// <param name="Reason">Why not, in one sentence, when it does not.</param>
public sealed record LinkOpensAvailability(bool Available, string Reason);

/// <summary>Port: where the request recorder hands an open, at once and without waiting; the Links slice's collector is behind it.</summary>
public interface ILinkOpenOffers
{
    /// <summary>Takes one open and returns at once.</summary>
    /// <param name="open">The open.</param>
    void Offer(LinkOpen open);
}

/// <summary>Port: where opens are kept. Writes arrive in batches, never from a request thread.</summary>
public interface ILinkOpens : ILinkOpensReader
{
    Task RecordAsync(IReadOnlyList<LinkOpen> opens, CancellationToken cancellation);
}

/// <summary>The read side of the opens: whether they are kept, and the kept days; the card's query takes this (ADR-115).</summary>
public interface ILinkOpensReader
{
    /// <summary>Whether opens are kept here, and why not when they are not.</summary>
    Task<LinkOpensAvailability> AvailabilityAsync(CancellationToken cancellation);

    /// <summary>The kept days since an instant, a code a day.</summary>
    Task<IReadOnlyList<LinkOpenDay>> DaysAsync(DateTimeOffset since, CancellationToken cancellation);
}

/// <summary>Opens folded a code a day, pure, so the adapters cannot fold one batch two ways.</summary>
public static class LinkOpenFolding
{
    /// <summary>The most paths a day keeps for one code; a code is sent for five links, and a scanner that walks the site is cut here.</summary>
    public const int PathsKept = 12;

    /// <summary>A batch folded into one delta a code a day, oldest day first.</summary>
    /// <param name="opens">The batch.</param>
    public static IReadOnlyList<LinkOpenDay> Fold(IReadOnlyList<LinkOpen> opens) =>
        [.. opens
            .GroupBy(open => (Day: open.At.UtcDateTime.ToString("yyyy-MM-dd"), open.Code))
            .Select(group => new LinkOpenDay(
                group.Key.Day,
                group.Key.Code,
                group.Min(open => open.At),
                group.Max(open => open.At),
                group.Count(),
                Top(group.GroupBy(open => open.Path, StringComparer.Ordinal).ToDictionary(paths => paths.Key, paths => paths.Count(), StringComparer.Ordinal))))
            .OrderBy(day => day.Day, StringComparer.Ordinal)
            .ThenBy(day => day.Code, StringComparer.Ordinal)];

    /// <summary>Two path counts added, the busiest paths kept up to the limit.</summary>
    /// <param name="kept">What the row holds.</param>
    /// <param name="added">What the batch adds.</param>
    public static IReadOnlyDictionary<string, int> Merge(IReadOnlyDictionary<string, int> kept, IReadOnlyDictionary<string, int> added)
    {
        var merged = new Dictionary<string, int>(kept, StringComparer.Ordinal);
        foreach (var (path, count) in added)
        {
            merged[path] = merged.GetValueOrDefault(path) + count;
        }
        return Top(merged);
    }

    /// <summary>The busiest paths up to the limit, the floor every reader of a stored row applies.</summary>
    /// <param name="paths">The paths with a count each.</param>
    public static IReadOnlyDictionary<string, int> Top(IEnumerable<KeyValuePair<string, int>> paths) =>
        paths.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(PathsKept)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}

/// <summary>Opens kept in this process's memory: what a container with no document store has, gone with the process.</summary>
public sealed class MemoryLinkOpens : ILinkOpens
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Day, string Code), LinkOpenDay> _days = new();

    public Task<LinkOpensAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new LinkOpensAvailability(true, "kept in this process's memory until it restarts"));

    public Task RecordAsync(IReadOnlyList<LinkOpen> opens, CancellationToken cancellation)
    {
        lock (_gate)
        {
            foreach (var delta in LinkOpenFolding.Fold(opens))
            {
                var key = (delta.Day, delta.Code);
                _days[key] = _days.TryGetValue(key, out var kept)
                    ? kept with
                    {
                        First = delta.First < kept.First ? delta.First : kept.First,
                        Last = delta.Last > kept.Last ? delta.Last : kept.Last,
                        Opens = kept.Opens + delta.Opens,
                        Paths = LinkOpenFolding.Merge(kept.Paths, delta.Paths),
                    }
                    : delta;
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LinkOpenDay>> DaysAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        string day = since.UtcDateTime.ToString("yyyy-MM-dd");
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LinkOpenDay>>([.. _days.Values.Where(kept => string.CompareOrdinal(kept.Day, day) >= 0).OrderBy(kept => kept.Day, StringComparer.Ordinal)]);
        }
    }
}
// #endregion link-open
