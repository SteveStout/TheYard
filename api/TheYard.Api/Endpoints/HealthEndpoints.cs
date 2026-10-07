using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace TheYard.Api;

/// <summary>
/// Liveness, readiness, the health report and the running build: what the
/// container's own check, the deploy's verify step and the Admin tab read.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Maps /healthz, /readyz, /api/health and /api/version.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        #region version-endpoint
        // Read once at startup, not per request: these are baked into the image and
        // cannot change while the process lives (ADR-005).
        app.MapGet("/api/version", Version)
            .WithName("GetVersion")
            .WithTags("Health")
            .WithSummary("The build this container was made from");
        #endregion version-endpoint

        #region probes
        // Three endpoints, three audiences. /healthz is the container's HEALTHCHECK
        // and answers only "the process is up". /readyz is the deploy's Verify step
        // and answers 503 until the files are in place. /api/health is the Admin tab
        // and carries the timings. A process can be alive and not yet ready, and the
        // orchestrator treats those differently (ADR-010).
        app.MapGet("/healthz", Liveness)
            .WithName("Liveness")
            .WithTags("Health")
            .WithSummary("Is the process up")
            .WithDescription("The container's own health check. Answers ok the moment the process is listening and says nothing else.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain");

        // Only the checks that gate it, and only those get run: the database probe is
        // two SQL statements whose answer readiness discards, and this endpoint is
        // polled by the orchestrator and by every deploy.
        app.MapGet("/readyz", Readiness)
            .WithName("Readiness")
            .WithTags("Health")
            .WithSummary("Should traffic be sent here")
            .WithDescription("The deploy's verify step. Only the checks that gate readiness run, and the database is not one "
                + "of them: a container whose store is gone still serves the catalogue from files.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/api/health", Health)
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Every check, timed, and the build that answered")
            .WithDescription("The Admin tab's health card. One check per store, named by the store, so a container running "
                + "both says which one is unavailable.");
        #endregion probes

        return app;
    }

    private static Ok<BuildInfo> Version(BuildInfo build) => TypedResults.Ok(build);

    private static ContentHttpResult Liveness() =>
        TypedResults.Text("ok");

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> Readiness(HostPaths paths, Backends backends) =>
        (await RunChecksAsync(paths, backends, readinessOnly: true)).Where(check => check.GatesReadiness).All(check => check.Status == "pass")
            ? TypedResults.Text("ready")
            : TypedResults.Problem(detail: "A file this site cannot run without is missing, or its catalogue is still loading; the health report says which.", statusCode: 503, title: "Not ready");

    private static async Task<Ok<HealthReport>> Health(HostPaths paths, Backends backends, BuildInfo build, HostStart start, KeepWarmState keepWarm)
    {
        var checks = await RunChecksAsync(paths, backends);
        return TypedResults.Ok(
            new HealthReport(
                checks.All(c => c.Status == "pass") ? "healthy" : "degraded",
                (long)(Clocks.UtcNow() - start.At).TotalSeconds,
                build.Version,
                build.Commit,
                checks,
                keepWarm.Loop is null ? null : KeepWarmReading.Of(keepWarm.Loop.Last)));
    }

    #region health-checks
    // Each probe is timed and each answer is a value, never an exception: a
    // health endpoint that throws tells an orchestrator nothing. The checks are
    // deliberately about the files this app cannot run without.
    private static async Task<HealthCheckEntry[]> RunChecksAsync(HostPaths paths, Backends backends, bool readinessOnly = false)
    {
        // Each probe is timed: the Admin tab shows the milliseconds beside the check,
        // so a slow disk or a slow lookup shows up before it fails (ADR-010, second pass).
        // Awaited, because the document store's probe is two point reads over the
        // network and a health check that blocked a thread on them would be the
        // defect the ports record describes (ADR: The ports learn to wait).
        async Task<HealthCheckEntry> Check(string name, Func<Task<bool>> probe, string detail, bool gatesReadiness = true)
        {
            if (readinessOnly && !gatesReadiness)
            {
                // Not asked, so not run. The caller filters these out anyway; this
                // is what stops the probe behind them from happening at all.
                return new HealthCheckEntry(name, "pass", "not asked", 0, gatesReadiness);
            }

            var clock = Stopwatch.StartNew();
            try { return new HealthCheckEntry(name, await probe() ? "pass" : "fail", detail, clock.ElapsedMilliseconds, gatesReadiness); }
            catch (Exception ex) { return new HealthCheckEntry(name, "fail", ex.GetType().Name, clock.ElapsedMilliseconds, gatesReadiness); }
        }
        var checks = new List<HealthCheckEntry>
        {
            await Check("dataset file", () => Task.FromResult(File.Exists(paths.DataPath)), "data/vehicles.json present"),
            await Check("docs", () => Task.FromResult(File.Exists(Path.Combine(paths.RepoRoot, "docs", "hosting", "HOSTING.md"))), "served documents findable"),
            await Check("photo manifest", () => Task.FromResult(File.Exists(paths.ManifestPath)), "image manifest present"),
            // The process listens before its catalogue is in (Startup.cs, listen-first),
            // so ready means the default store's catalogue has loaded: the thing a
            // deploy, and a blue-green swap, must wait for before sending a visitor.
            await Check("catalogue", () => Task.FromResult(backends.Default.CatalogueLoaded), "the default store's catalogue is loaded"),
        };
        // One check per store, named by the store, so a container running both
        // says which one is unavailable. The default store's check is called
        // "database", because the deploy's Verify step and the Admin tab's card both
        // read that name.
        foreach (var backend in backends.All)
        {
            checks.Add(await Check(
                ReferenceEquals(backend, backends.Default) ? "database" : $"database ({backend.Key})",
                backend.Probe,
                // The reason is in the log, not in this response. A health endpoint
                // is public on purpose, and an exception message from a storage
                // failure is typically a filesystem path: exactly the map of the
                // inside of the process that ProblemHandler refuses to draw.
                backend.Database.Ready
                    ? $"the seed catalogue is in the store ({backend.Name})"
                    : $"{backend.Name} is unavailable, serving the catalogue from files; "
                        + "the reason is in the log",
                // The one check that does not gate readiness, which is the whole
                // point of the fallback. A container with no database still serves
                // the catalogue, the filters, the photos and the bidding; the only
                // thing it loses is bids outliving the process. Reporting itself
                // not ready would take a working site out of service: the deploy's
                // `curl -fsS /readyz` would fail against a site serving every
                // vehicle perfectly well.
                gatesReadiness: false));
        }
        return checks.ToArray();
    }
    #endregion health-checks
}
