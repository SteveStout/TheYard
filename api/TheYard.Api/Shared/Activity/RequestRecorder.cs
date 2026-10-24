// One request, filed once: in the timing ring, as a hit on the activity card, and as a kept
// event in the log that outlives the container. The request pipeline's outermost step hands every
// request here when it has finished, with the time it took (Shared/Wiring/RequestPipeline.cs).

using TheYard.Application.Features.Links;

namespace TheYard.Api.Shared.Activity;

// #region request-recorder
/// <summary>
/// Files one finished request, unless it is the Admin tab watching itself, the page sweep or the
/// keep-warm loop talking to its own container (ADR: What the database is actually doing),
/// (ADR: Logs that outlive the container) and (ADR: Site activity, and the line an address does
/// not cross). Nothing here waits on a store: each record is offered and forgotten.
/// </summary>
public sealed class RequestRecorder(
    RequestRingBuffer requestLog,
    Backends backends,
    LogCollector logCollector,
    VisitorTokens visitorTokens,
    ActivityCollector activity,
    ILinkOpenOffers links,
    SiteHosts sites)
{
    private readonly string[] _siteHosts = [.. sites.All.Select(site => site.Host)];

    /// <summary>
    /// The endpoints that exist to be read by the Admin tab, which are left out of the Admin tab's
    /// own numbers. An open Admin tab polls them every thirty seconds; left in, they would push
    /// every real request out of the ring inside an hour, and the cloud cards' reads, which wait
    /// seconds on Azure Monitor, Cost Management and Netlify when their window is cold, would put
    /// seconds into the first screen's usual answer time, which a tab left open overnight once did
    /// (ADR: One reading for the first screen, the addendum on the Admin tab's reads).
    /// Every address under /api/admin/ but the self-test, which is the deliberate failure that
    /// proves the error path works and which the timing section should show, and the four reads
    /// outside it that are the site watching itself.
    /// </summary>
    private static readonly HashSet<string> ObservabilityReads = new(StringComparer.Ordinal)
    {
        "/api/errors",
        "/api/health",
        "/readyz",
        "/healthz",
    };

    /// <summary>
    /// Whether a request is one of the Admin tab's reads, or another read of the site by itself. A read
    /// only: a write under /api/admin/ (a usage document posted, a blog post, a sweep started) and every
    /// refusal of one stay in the ring and the kept log, because the operator's key is tried against them.
    /// </summary>
    public static bool IsObservabilityRead(string method, string path) =>
        HttpMethods.IsGet(method)
        && (ObservabilityReads.Contains(path)
            || (path.StartsWith("/api/admin/", StringComparison.Ordinal)
                && !path.StartsWith("/api/admin/selftest/", StringComparison.Ordinal)));

    /// <summary>Files one finished request and the time it took.</summary>
    /// <param name="context">The request, after its response was written.</param>
    /// <param name="elapsed">How long the caller waited.</param>
    public void Record(HttpContext context, TimeSpan elapsed)
    {
        // A visit to /resume is recorded as the door it came through, /resume/linkedin, which
        // the handler left on the request; every other request is its own path (ResumeDoor.cs).
        string path = context.Items.TryGetValue(ResumeDoor.ItemKey, out var door) && door is string doorPath
            ? doorPath
            : context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        if (IsObservabilityRead(context.Request.Method, path))
        {
            return;
        }

        // The page sweep asks this container for every address it serves at every roll (ADR:
        // Every page, checked at every roll); that is the container talking to itself.
        if (context.Request.Headers.ContainsKey(PageStatusRunner.CheckHeader))
        {
            return;
        }

        // The keep-warm loop's reads (ADR: Kept awake) stay out of the ring and the kept log, and
        // still reach the activity card below, marked as the site reading itself.
        bool keptWarm = context.Request.Headers.ContainsKey(KeepWarm.Header);
        string store = backends.For(context).Key;
        if (!keptWarm)
        {
            requestLog.Record(new RequestEntry(
                DateTimeOffset.UtcNow,
                context.Request.Method,
                // Bounded, because a request line can be eight kilobytes and five hundred of
                // those is four megabytes of ring nobody asked for.
                path.Length > 200 ? path[..200] + "..." : path,
                context.Response.StatusCode,
                (long)elapsed.TotalMilliseconds,
                // Which store served it, read the way the request itself was routed, so the
                // comparison card can split one ring two ways.
                store));
        }

        // #region activity-hook
        // The same request as a hit for the activity card. What the hit carries, and what it
        // cannot, is decided in Activity.cs and in the type. The token and the network are made
        // once here and shared with the kept log below: one keyed hash per request, not two.
        if (!Hits.Counts(path))
        {
            return;
        }

        string address = VisitorTokens.AddressOf(context);
        var at = DateTimeOffset.UtcNow;
        // #region link-open-hook
        // A tagged link's open, by its code and this path, when the request is one (the rule is
        // TaggedRequests, in Application): the resume's door, the pass-through to GitHub or
        // LinkedIn, or the page's own report of the view it opened on. The code is six opaque
        // characters; who it was sent to is the operator's lookup, off the site (ADR: Tagged
        // links, who opened what).
        if (TaggedRequests.OpenOf(
                context.Request.Method,
                path,
                context.Request.Query[LinkCodes.QueryKey].FirstOrDefault(),
                context.Request.Query["path"].FirstOrDefault(),
                context.Request.Headers.UserAgent.FirstOrDefault(),
                at,
                store,
                context.Response.StatusCode) is { } open)
        {
            links.Offer(open);
        }
        // #endregion link-open-hook
        string token = visitorTokens.TokenFor(address, at);
        string network = VisitorTokens.NetworkOf(address);
        activity.Offer(Hits.For(visitorTokens, address, at, token, network, path, store, context.Request.Headers.UserAgent.FirstOrDefault(), Hits.SourceOf(context.Request.Headers.Referer.FirstOrDefault(), path, context.Request.Host.Host, _siteHosts)));
        // #endregion activity-hook

        // #region kept-logs-hook
        // And once more as a kept event, with its method, status and duration. The page's own
        // files and the photos are left out for the reason the activity feature leaves them out.
        if (keptWarm)
        {
            return;
        }

        logCollector.Offer(LogEvents.Request(
            at,
            context.Request.Method,
            path,
            context.Response.StatusCode,
            (long)elapsed.TotalMilliseconds,
            store,
            token,
            network,
            context.TraceIdentifier));
        // #endregion kept-logs-hook
    }
}
// #endregion request-recorder
