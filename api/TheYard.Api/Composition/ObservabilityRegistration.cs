using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The rings behind the Admin tab, the kept log that outlives the container, and the
/// request hook that files every request once (ADR: What the database is actually
/// doing), (ADR: Logs that outlive the container) and (ADR: Site activity, and the
/// line an address does not cross).
/// </summary>
public static class ObservabilityRegistration
{
    /// <summary>Registers the rings, the kept log and the request hook.</summary>
    public static void AddTheYardObservability(this WebApplicationBuilder builder, YardComposition host)
    {
        var backends = host.Backends;
        var sqlLog = host.SqlLog;
        var storeLog = host.StoreLog;
        var httpContextAccessor = host.HttpContextAccessor;
        var currentRequest = host.CurrentRequest;
        var cosmos = host.Cosmos;
        #region admin-rings
        // The three rings behind the Admin tab's new sections, registered here because
        // the SQL one has to exist before the context factory that feeds it. All three
        // are this process's memory and nothing else: they empty on every roll, which
        // the page says out loud (ADR: What the database is actually doing).
        var logLog = new LogRingBuffer(300);
        var requestLog = new RequestRingBuffer(RingSizes.RequestRing);
        builder.Services.AddSingleton(logLog);
        builder.Services.AddSingleton(requestLog);
        builder.Services.AddSingleton(sqlLog);
        builder.Services.AddSingleton<ISqlLog>(sqlLog);
        builder.Services.AddSingleton(storeLog);
        builder.Services.AddSingleton<IStoreLog>(storeLog);
        builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        builder.Services.AddSingleton<ICurrentRequest>(currentRequest);
        builder.Logging.AddProvider(new RingBufferLoggerProvider(logLog));

        // #region kept-logs-wiring
        // The kept log (ADR: Logs that outlive the container): the same three things
        // the rings hold, written to the document store off the request path and
        // kept for three years. One collector, fed by the request hook below and by a
        // logging provider that takes this application's warnings and errors, on a
        // container with no document store configured it is wired to nothing and
        // the card says so. The provider reads the store and the request a line
        // belongs to from the current request when there is one.
        ILogStore logStore = cosmos is not null ? new CosmosLogStore(cosmos) : NullLogStore.Instance;
        // Reset links (ADR: Accounts and per-user bids, addendum of 14 September):
        // the GUID a link carries is kept in the document store for the hour and
        // forgotten on use; without one, in this process's memory, which is what
        // the test host and a developer's machine get.
        IResetLinks resetLinks = cosmos is not null ? new CosmosResetLinks(cosmos) : new MemoryResetLinks();
        builder.Services.AddSingleton(resetLinks);
        // This site as a visitor reaches it, for links written into an email.
        // Behind the edge the request's own host is the origin, which is not an
        // address anybody should be sent; unset, the request's host is used, which
        // is right on a developer's machine and on the test host.
        string? siteUrl = builder.Configuration["Site:Url"];
        var logCollector = new LogCollector(logStore, builder.Configuration.GetValue("Logs:DrainSeconds", LogCollector.DefaultIntervalSeconds));
        builder.Services.AddSingleton(logCollector);
        builder.Services.AddHostedService(_ => logCollector);
        // #region kept-rings-wiring
        // The four public lists on the Admin tab are rings, and a roll empties them.
        // Each entry a ring takes is also offered to the collector above, as the JSON
        // the ring's own endpoint serves, so the cards can read a day, a week or a
        // month back from the document store (ADR: Logs that outlive the container, the addendum on the cards). The
        // two error rings are made further down and are wired where they are made.
        var keptRings = new KeptRingWriter(logCollector, backends.Default.Key);
        sqlLog.Kept = statement => keptRings.Keep(KeptRings.Sql, statement.At, statement);
        storeLog.Kept = operation => keptRings.Keep(KeptRings.Store, operation.At, operation);
        logLog.Kept = line => keptRings.Keep(KeptRings.Log, line.At, line);
        builder.Services.AddSingleton(new KeptRingReader(logCollector, backends.Default.Key, backends.Named("cosmos")?.Name ?? backends.Default.Name));
        // #endregion kept-rings-wiring
        builder.Logging.AddProvider(new CollectorLoggerProvider(logCollector, () =>
        {
            var http = httpContextAccessor.HttpContext;
            return http is null
                ? ("", "", "")
                : (backends.For(http).Key, http.Request.Path.HasValue ? http.Request.Path.Value! : "/", http.TraceIdentifier);
        }));
        // #endregion kept-logs-wiring

        // The endpoints that exist to be read by the Admin tab, which are excluded from
        // the Admin tab's own numbers.
        //
        // Named one by one rather than matched on the /api/admin/ prefix, because
        // /api/admin/selftest/exception is not an observability read: it is the
        // deliberate failure that proves the error path works, and it is exactly the
        // request the timing section should be showing.
        var observabilityReads = new HashSet<string>(StringComparer.Ordinal)
        {
            "/api/admin/sql",
            "/api/admin/store",
            "/api/admin/logs",
            "/api/admin/metrics",
            "/api/admin/peer",
            "/api/admin/experiment",
            "/api/admin/proof",
            "/api/admin/azure",
            "/api/admin/telemetry",
            "/api/admin/activity",
            "/api/admin/activity/visitors",
            "/api/admin/pages",
            "/api/admin/machines",
            "/api/admin/logs/kept",
            "/api/admin/kept",
            "/api/admin/reset-links",
            "/api/errors",
            "/api/health",
            "/readyz",
            "/healthz",
        };

        // The activity feature's two pieces are wired once the signing key exists and
        // once the host is built; the request hook reads them from the composition when
        // a request arrives (ADR: Site activity, and the line an address does not cross).
        // The loop that keeps every read warm (ADR: Kept awake), wired after the app is
        // built; the health report reads it through this.
        var keepWarmState = new KeepWarmState();
        builder.Services.AddSingleton(keepWarmState);

        // One request, timed and filed, unless it is the Admin tab watching itself.
        //
        // The observability endpoints are excluded because otherwise they take the
        // page over. An open Admin tab polls seven endpoints every thirty seconds, and
        // /api/health runs two SQL statements each time it is asked; left in, that is
        // fourteen requests a minute of /api/admin/* and enough health-check SELECTs to
        // push every real statement out of a two-hundred slot ring inside an hour. The
        // section would show nothing but itself, which is the observer effect with a
        // literal implementation.
        void RecordRequest(HttpContext context, TimeSpan elapsed)
        {
            string path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
            if (observabilityReads.Contains(path))
            {
                return;
            }

            // The page sweep asks this container for every address it serves, at every
            // roll (ADR: Every page, checked at every roll). That is this container
            // talking to itself: left in, ninety self-requests would push a morning of
            // real traffic out of the ring and add a visitor to the activity card who
            // is this container.
            if (context.Request.Headers.ContainsKey(PageStatusRunner.CheckHeader))
            {
                return;
            }

            // The keep-warm loop's reads (ADR: Kept awake) stay out of the ring and the
            // kept log: two hundred slots of the container reading itself every four
            // minutes would be the speed tile timing the loop, not a visitor. They still
            // reach the activity card below, marked as the site reading itself.
            bool keptWarm = context.Request.Headers.ContainsKey(KeepWarm.Header);
            if (!keptWarm)
            {
                requestLog.Record(new RequestEntry(
                    DateTimeOffset.UtcNow,
                    context.Request.Method,
                    // Bounded, because a request line can be eight kilobytes and five
                    // hundred of those is four megabytes of ring nobody asked for.
                    path.Length > 200 ? path[..200] + "..." : path,
                    context.Response.StatusCode,
                    (long)elapsed.TotalMilliseconds,
                    // Which store served it, read the same way the request itself was
                    // routed, so the comparison card can split one ring two ways.
                    backends.For(context).Key));
            }

            // #region activity-hook
            // And the same request as a hit for the activity card, offered to the
            // collector and forgotten: nothing here waits on a store. What the hit
            // carries, and what it cannot, is decided in Activity.cs and in the type.
            // The token and the network are made once here and shared with the kept
            // log below, so a request pays for one keyed hash and not two.
            if (host.VisitorTokens is not { } visitorTokens || !Hits.Counts(path))
            {
                return;
            }

            string address = VisitorTokens.AddressOf(context);
            var at = DateTimeOffset.UtcNow;
            string token = visitorTokens.TokenFor(address, at);
            string network = VisitorTokens.NetworkOf(address);
            string store = backends.For(context).Key;
            host.ActivityCollector?.Offer(Hits.For(visitorTokens, address, at, token, network, path, store, context.Request.Headers.UserAgent.FirstOrDefault(), Hits.SourceOf(context.Request.Headers.Referer.FirstOrDefault(), path, context.Request.Host.Host)));
            // #endregion activity-hook

            // #region kept-logs-hook
            // And once more as a kept event: the same request, the same token and
            // network, with its method, status and duration, offered to the log
            // collector and forgotten. The page's own files and the photos are left
            // out for the reason the activity feature leaves them out.
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
        #endregion admin-rings
        host.LogLog = logLog;
        host.RequestLog = requestLog;
        host.LogCollector = logCollector;
        host.KeptRings = keptRings;
        host.KeepWarm = keepWarmState;
        host.SiteUrl = siteUrl;
        host.RecordRequest = RecordRequest;
    }
}
