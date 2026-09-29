using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// What happens once the host is built and before it serves: the loggers attached,
/// the stores said out loud, the default catalogue warmed, the error rings wired to
/// the kept log, the page sweep and the keep-warm loop started at the roll.
/// </summary>
public static class Startup
{
    /// <summary>Warms the default store and starts what runs beside the requests.</summary>
    public static async Task StartTheYardAsync(this WebApplication app, YardComposition host)
    {
        var sqlBackend = host.SqlBackend;
        var cosmos = host.Cosmos;
        var backends = host.Backends;
        var yard = host.Relational;
        string? configuredDatabase = host.ConfiguredDatabase;
        string scratchDatabase = host.ScratchDatabase;
        string? configuredSigningKey = host.ConfiguredSigningKey;
        var keepWarmState = host.KeepWarm;
        var keptRings = host.KeptRings;
        var hostStart = host.Start;
        var errorRings = host.ErrorRings;
        host.ActivityCollector = app.Services.GetRequiredService<ActivityCollector>();

        // Entity Framework's own command log goes where every other log line goes,
        // which the Admin tab's log section and a test both rely on; the document
        // store writes one line per operation to the same place from here on
        // (ADR: What the store is actually doing, addendum).
        host.HostLoggers = app.Services.GetRequiredService<ILoggerFactory>();
        (sqlBackend.Contexts as ContextFactory)?.Attach(host.HostLoggers);
        if (cosmos is not null)
        {
            cosmos.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosStore>();
        }
        // The promise the accounts record makes: an invented signing key is said out
        // loud, because its consequence is every session ending when this process does
        // (ADR: Accounts and per-user bids). Nothing about the key itself is logged.
        if (configuredSigningKey is null)
        {
            app.Logger.LogWarning(
                "No usable Auth:SigningKey is configured (none, a placeholder, or under {Bytes} bytes); "
                + "this process signs cookies with a key it invented at startup, so every session ends with it",
                TokenIssuer.MinimumKeyBytes);
        }

        // The address the proof talks to itself on, read once the server is up.
        // Kestrel reports the wildcard it bound ("http://[::]:8080"), which is not
        // an address a client can dial, so the host becomes the loopback.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            string? bound = app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal)) ?? app.Urls.FirstOrDefault();
            if (bound is not null)
            {
                string dialable = bound.Replace("://+:", "://127.0.0.1:", StringComparison.Ordinal).Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal);
                host.SelfUrl = Uri.TryCreate(dialable, UriKind.Absolute, out var parsed)
                    ? new UriBuilder(parsed) { Host = parsed.HostNameType == UriHostNameType.Dns && parsed.Host != "localhost" ? parsed.Host : "127.0.0.1" }.Uri.ToString()
                    : null;
            }
        });

        foreach (var backend in backends.All)
        {
            if (backend.Database.Ready)
            {
                app.Logger.LogInformation("Database ready: {Note}", backend.Database.Note);
            }
            else
            {
                // "The store" rather than "the database": Prepare also reads the seed
                // files, so this line covers a missing dataset as well as a database that
                // will not open, and naming only one of them sends the next person to the
                // wrong place (the staff review, 2026-09-03).
                // The exception goes in the exception slot, not into the template. What is
                // in the template reaches the Admin tab's log section, which is public; what
                // is in the exception slot reaches the console and Application Insights,
                // and the Admin tab shows only its type. A SqlException here says the server
                // name, the login name and this container's IP address.
                app.Logger.LogError(
                    backend.Database.Failure,
                    "The store could not be prepared, which covers both the database and the seed files it fills from. The catalogue is being served from the JSON files and bids will not outlive this process: {Note}",
                    backend.Database.Note);
            }
        }

        if (configuredDatabase is null && yard.Provider == YardProvider.Sqlite)
        {
            app.Logger.LogWarning(
                "No ConnectionStrings:Yard is configured, so this process is using a scratch database in the system temp folder and will delete it on shutdown");
            // The path itself at Debug, which the Admin tab's log section does not
            // capture. A temp path names the account the process runs as, and that page
            // is public.
            app.Logger.LogDebug("The scratch database is at {Path}", scratchDatabase);
            // A scratch database belongs to one process, so it goes when the process
            // does. The pool has to be emptied first or the handle is still open and
            // the delete fails on Windows.
            app.Lifetime.ApplicationStopped.Register(() =>
            {
                // SQLite writes three files, not one: the database, the write-ahead log
                // and the shared-memory index. Deleting only the first leaves the other
                // two behind, and this runs whether or not the store came up, because
                // the half-created file is exactly the case that used to leak (the
                // staff review, 2026-09-03).
                foreach (string leftover in new[]
                         {
                             scratchDatabase,
                             scratchDatabase + "-wal",
                             scratchDatabase + "-shm",
                             scratchDatabase + "-journal",
                         })
                {
                    try
                    {
                        File.Delete(leftover);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // A leftover scratch file is litter, not a reason to fail a shutdown.
                    }
                }
            });
        }

        // Materialize the inventory and replay the bids now, so a bad dataset fails
        // the process at startup, visibly, and not as a 500 on the first request, and
        // so no visitor's request is the one that waits for the store. This is the
        // warm-up the ports record leans on: after these two lines every synchronous
        // read in the application is reading a task that has already finished
        // (ADR: The ports learn to wait).
        // The default store first, before anything is served, which is the warm-up
        // the ports record leans on. The other store is warmed after the container
        // is ready, in the background, one after the other rather than both at once:
        // the container has one vCPU, and two expansions of a hundred thousand
        // records racing each other would both take longer and neither number would
        // be that store's own. Off by default and on in the deploy, because a test
        // run boots ten applications at once and ten second expansions nobody asks
        // for is memory the machine running the suite does not have to give; a store
        // nobody warmed warms itself on its first request, which is the Lazy the
        // inventory service has always had (ADR: One container, both stores).
        await backends.Default.Startup.Time("catalogue", backends.Default.Inventory.WarmAsync);
        await backends.Default.Startup.Time("bids", backends.Default.Bids.LoadAsync);
        backends.Default.Startup.Ready();
        if (app.Configuration.GetValue("Store:WarmOthers", false))
        {
            _ = Task.Run(async () =>
            {
                foreach (var backend in backends.All.Where(candidate => !ReferenceEquals(candidate, backends.Default)))
                {
                    try
                    {
                        await backend.Startup.Time("catalogue", backend.Inventory.WarmAsync);
                        await backend.Startup.Time("bids", backend.Bids.LoadAsync);
                        backend.Startup.Ready();
                    }
                    catch (Exception ex)
                    {
                        // The type only; the message can carry a host name and this
                        // line reaches the public log section.
                        app.Logger.LogError("Warming the {Store} store failed with {Exception}; its first visitor will try again", backend.Name, ex.GetType().Name);
                    }
                }
            });
        }

        hostStart.Mark();
        var errorLog = errorRings.Server;
        // #region two-rings
        // Browser reports get their own fifty slots rather than sharing the server's.
        //
        // One list was the decision (ADR: Error handling, one shape everywhere)
        // and it still is: the Admin tab shows them merged, because one place to look is
        // the point. What changed is where they are kept. POST /api/errors/client is
        // anonymous by design, and while the message and the stack were bounded, the
        // number of reports was not, so fifty posts from anybody evicted every real
        // server error from the page an operator would use to diagnose an outage. A
        // separate ring means a flood of reports can only push out other reports.
        var browserErrors = errorRings.Browser;
        // #endregion two-rings
        errorLog.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);
        browserErrors.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);

        // #region page-status-wiring
        // The sweep over every address this container serves. It dials the loopback
        // the proof dials, and it is null until the server is listening, which is what
        // keeps it from running under the test host: the suite drives RunAsync with
        // the test server's own client instead (ADR: Every page, checked at every roll).
        var pageStatus = app.Services.GetRequiredService<PageStatusRunner>();

        // Every roll carries a check of the thing that was just rolled. The address
        // is read from the server when the sweep runs rather than from the variable
        // the proof's callback fills: these callbacks run in the reverse of the order
        // they were registered, so this one runs first, and the first cut of it never
        // swept anything (SelfAddress, and the browser suite that found it).
        app.Lifetime.ApplicationStarted.Register(() => pageStatus.TryStart("roll"));
        // And once more when the process has settled (1.0.3.8): the roll's sweep runs
        // while the catalogues are warming, and a reading taken then is a reading of
        // the start, not of the site. The second is what the tile shows until
        // somebody asks for another.
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            await Task.Delay(PageStatusRunner.SecondSweep);
            pageStatus.TryStart("settled");
        }));

        // #region keep-warm-wiring
        // Every public read, on both stores, every four minutes for as long as the
        // container runs (ADR: Kept awake). On where App Service runs the site
        // (WEBSITE_SITE_NAME is App Service's own) and off everywhere else, the test
        // host included; KeepWarm:Enabled says otherwise either way. It dials the
        // loopback the page sweep dials, through the whole pipeline.
        if (app.Configuration.GetValue("KeepWarm:Enabled", !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))))
        {
            var keepWarmLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<KeepWarm>();
            HttpClient? keepWarmClient = null;
            var loop = new KeepWarm(
                async cancellation =>
                {
                    keepWarmClient ??= SelfAddress.Of(app.Services) is { } dialable
                        ? new HttpClient(new HttpClientHandler { UseCookies = false })
                        {
                            BaseAddress = new Uri(dialable),
                            Timeout = TimeSpan.FromSeconds(30),
                        }
                        : null;
                    return keepWarmClient is null
                        ? new KeepWarmPass(DateTimeOffset.UtcNow, 0, 0, 0, null)
                        : await KeepWarmReads.RunAsync(keepWarmClient, backends.All.Select(backend => backend.Key).ToList(), TimeProvider.System, keepWarmLog, cancellation);
                },
                TimeProvider.System,
                KeepWarm.RandomStagger(),
                keepWarmLog);
            keepWarmState.Loop = loop;
            app.Lifetime.ApplicationStarted.Register(() => _ = loop.StartAsync(CancellationToken.None));
            app.Lifetime.ApplicationStopping.Register(() => loop.StopAsync(CancellationToken.None).GetAwaiter().GetResult());
        }
        // #endregion keep-warm-wiring
        // #endregion page-status-wiring
    }
}
