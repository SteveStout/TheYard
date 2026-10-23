using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// The rings behind the Admin tab, the kept log that outlives the container, and the
/// request hook that files every request once (ADR: What the database is actually
/// doing), (ADR: Logs that outlive the container) and (ADR: Site activity, and the
/// line an address does not cross).
/// </summary>
public static class ObservabilityRegistration
{
    /// <summary>Registers the rings, the kept log and the request hook.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="backends">Every store, for the store a line or a request is filed under.</param>
    /// <param name="cosmos">The document store the kept log and the reset links are written to, or null.</param>
    /// <param name="sqlLog">The relational store's ring, made with the store.</param>
    /// <param name="storeLog">The document store's ring, made with the store.</param>
    /// <param name="accessor">The accessor the log provider reads the current request through.</param>
    /// <param name="currentRequest">The request describer the store logs file each operation under.</param>
    public static void AddTheYardObservability(
        this WebApplicationBuilder builder,
        Backends backends,
        CosmosStore? cosmos,
        SqlRingBuffer sqlLog,
        StoreRingBuffer storeLog,
        HttpContextAccessor accessor,
        HttpCurrentRequest currentRequest)
    {
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
        builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
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
        // Reset links (ADR: Accounts and per-user bids, the addendum on what the link looks like):
        // the GUID a link carries is kept in the document store for the hour and
        // forgotten on use; without one, in this process's memory, which is what
        // the test host and a developer's machine get.
        IResetLinks resetLinks = cosmos is not null ? new CosmosResetLinks(cosmos) : new MemoryResetLinks();
        builder.Services.AddSingleton(resetLinks);
        var logCollector = new LogCollector(logStore, builder.Configuration.GetValue("Logs:DrainSeconds", LogCollector.DefaultIntervalSeconds));
        builder.Services.AddSingleton(logCollector);
        builder.Services.AddHostedService(_ => logCollector);
        // #region kept-rings-wiring
        // The four public lists on the Admin tab are rings, and a roll empties them.
        // Each entry a ring takes is also offered to the collector above, as the JSON
        // the ring's own endpoint serves, so the cards can read a day, a week or a
        // month back from the document store (ADR: Logs that outlive the container, the addendum on the cards). The
        // two error rings are wired to it when the host starts (ErrorRingKeeping).
        var keptRings = new KeptRingWriter(logCollector, backends.Default.Key);
        builder.Services.AddSingleton(keptRings);
        sqlLog.Kept = statement => keptRings.Keep(KeptRings.Sql, statement.At, statement);
        storeLog.Kept = operation => keptRings.Keep(KeptRings.Store, operation.At, operation);
        logLog.Kept = line => keptRings.Keep(KeptRings.Log, line.At, line);
        builder.Services.AddSingleton(new KeptRingReader(logCollector, backends.Default.Key, backends.Named(StoreKeys.Document)?.Name ?? backends.Default.Name));
        // #endregion kept-rings-wiring
        builder.Logging.AddProvider(new CollectorLoggerProvider(logCollector, () =>
        {
            var http = accessor.HttpContext;
            return http is null
                ? ("", "", "")
                : (backends.For(http).Key, http.Request.Path.HasValue ? http.Request.Path.Value! : "/", http.TraceIdentifier);
        }));
        // #endregion kept-logs-wiring

        // The keep-warm loop's slot (ADR: Kept awake): filled when the loop starts at the roll,
        // read by the health report.
        builder.Services.AddSingleton(new KeepWarmState());

        // One request, timed and filed, unless it is the Admin tab watching itself
        // (RequestRecorder.cs). The pipeline's outermost step hands every request to it.
        builder.Services.AddSingleton<RequestRecorder>();
        #endregion admin-rings
    }
}
