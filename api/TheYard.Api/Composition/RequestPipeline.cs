using System.Diagnostics;
using Microsoft.Extensions.FileProviders;

namespace TheYard.Api;

/// <summary>
/// The request pipeline, in ASP.NET Core's own term: the middleware every request passes
/// through in this order, and every response passes back through in reverse, in the order
/// Microsoft documents for an app behind a proxy. Timing outermost; then the edge's
/// forwarded headers; the problem shape and the error record; the cache rules and the
/// catalogue's compression; the files, which end there; routing, where the health probes
/// end; then the user, the session's renewal and the store's warmth for whatever an
/// endpoint answers (ADR: The order of the request pipeline).
/// </summary>
public static class RequestPipeline
{
    /// <summary>Adds TheYard's middleware in its required order.</summary>
    public static WebApplication UseTheYardRequestPipeline(this WebApplication app, YardComposition host)
    {
        var tokens = host.Tokens;
        var errorLog = host.ErrorRings.Server;
        string imagesRoot = host.Paths.ImagesRoot;

        #region request-timing
        // Timing, and it has to be the outermost thing here.
        //
        // Outermost, above UseExceptionHandler, because unwinding runs inner to outer.
        // Placed below the handler, a request that threw would reach this finally
        // before the handler had written anything, so every failed request would be
        // recorded as a 200 with the handler's time left out. Here it records the
        // status the caller actually received and the whole time they waited.
        //
        // Above the handler it sees the status that was actually sent. Above
        // UseAuthentication too, so a request rejected with 401 is counted rather than
        // short-circuited before it ever reaches the ring.
        app.Use(async (context, next) =>
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await next();
            }
            finally
            {
                host.RecordRequest(context, Stopwatch.GetElapsedTime(start));
            }
        });
        #endregion request-timing

        // #region forwarded-headers
        // What: the visitor's address and scheme, read from the headers the edge and App
        // Service's front end add, and written onto the request. How: UseForwardedHeaders
        // with the options in ApiRegistration: X-Forwarded-For and X-Forwarded-Proto
        // only, counted back from the right as many hops as there are proxies we run
        // through, so whatever a visitor typed into those headers is never read.
        // Why here: before anything that reads the address or the scheme. The session
        // cookie's Secure flag, the activity card's visitor and the reset link all
        // read them, and before this each read the raw header in its own way.
        app.UseForwardedHeaders();
        // #endregion forwarded-headers

        // #region problem-shape
        // What: an unhandled exception becomes a 500 ProblemDetails, and a bare status
        // with no body gets one too. How: the handler is ProblemHandler (ADR-030), the
        // status pages fill in the rest. Why here: it can only catch what runs after
        // it, so it comes before everything but the timing and the forwarded headers.
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // #endregion problem-shape

        #region error-log
        // What: every 500 and every exception goes into the error record the Admin tab
        // reads. How: it records and rethrows rather than handling, so the exception
        // handler above still owns the response. Why here: directly inside the
        // exception handler, so it sees a failure in anything below it, signing in and
        // renewing a session included. It sat below both until the pipeline lane, and
        // an exception thrown while reading a token never reached the record.
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
                if (context.Response.StatusCode >= 500)
                {
                    errorLog.Record(context.Request.Path, context.Response.StatusCode, "server error response");
                }
            }
            catch (Exception ex)
            {
                // A caller who hung up is not a server error: the exception handler
                // answers it 499 and logs it at Information, so it is left out of
                // this record too, and the Admin tab's error count stays green.
                if (!ProblemHandler.CallerLeft(context, ex))
                {
                    // The type and the frames, never the message. This record is
                    // served at /api/errors with no sign-in, and an exception message
                    // is where a framework writes a filesystem path, a connection
                    // detail, or the value that broke a constraint; the exception
                    // handler keeps it out of a response for the same reason. The
                    // frames are source locations this repository already publishes.
                    // The message is not lost: it goes to the console and to
                    // Application Insights as a structured exception, behind a sign-in
                    // (ADR: Error handling, the addendum on frames).
                    errorLog.Record(context.Request.Path, 500, ex.GetType().Name, StackFrames.Of(ex));
                }
                throw;
            }
        });
        #endregion error-log

        #region cache-headers
        // Cache rules (ADR-015), from the shape of the address. Registered before the
        // files and the endpoints so its rule is in place on every response either writes. Vite names every
        // bundle file by a hash of its contents, so /assets/* can be kept for a year
        // and never goes stale: a new build has new names. Everything that can change
        // under the same address (the page, the API, the documents) says no-cache, so
        // a browser asks before reusing it. The photo set keeps its own one-day rule
        // below, and a response that already chose its rule is left alone.
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var response = context.Response;
                if (!response.Headers.ContainsKey("Cache-Control"))
                {
                    bool hashedBundleFile = context.Request.Path.StartsWithSegments("/assets")
                        && response.StatusCode == StatusCodes.Status200OK
                        && !(response.ContentType ?? "").StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
                    response.Headers.CacheControl = hashedBundleFile
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";
                }
                // The catalogue's two reads may be kept by the edge for a few seconds when the
                // answer is one every visitor would get; the browser's rule above is unchanged.
                CatalogueReads.LetTheEdgeKeep(context);
                return Task.CompletedTask;
            });
            await next();
        });
        #endregion cache-headers

        #region compression
        // What: compression runs on a branch of the pipeline that only the catalogue's
        // two reads enter (CatalogueReads.Covers: a GET of /api/vehicles or
        // /api/facets). Every other response, bids, accounts and the admin endpoints
        // included, is sent as it is.
        //
        // Why only these two: compressing an HTTPS response can leak a secret through
        // its size. That attack, BREACH, needs a secret (a session token, say) and text
        // the attacker chooses in the same compressed body; by sending many requests
        // and watching the length shrink, the attacker guesses the secret a character
        // at a time. The catalogue reads are the same public data for every visitor and
        // carry no secret in their bodies. A session travels in the Cookie header,
        // which this compression never touches.
        //
        // How: it sits here, before the endpoints, so it wraps the response body they
        // write; the files below are never these two reads, and the edge compresses
        // them on its own. The compressor itself is set up in ApiRegistration.
        app.UseWhen(context => CatalogueReads.Covers(context.Request), branch => branch.UseResponseCompression());
        #endregion compression

        #region static-files
        // What: the page, the hashed bundle files and the photos, straight from disk.
        // How: UseDefaultFiles turns "/" into "/index.html", and each UseStaticFiles
        // answers a file it has and ends the request there; anything else carries on.
        // Why here: ahead of routing, the user, the session and the store's warmth,
        // none of which a file needs. Microsoft's order puts static files right after
        // error handling for exactly this reason. Until the pipeline
        // lane they ran last, so every file waited behind all of that, and the photos,
        // which live under /api/images, waited for the catalogue to load after a roll.
        // The cache rules above still wrap them, because those are registered first.
        // The SPA fallback stays the last word (SpaRegistration.cs): it answers app
        // routes with index.html, while an address that looks like a file stays a
        // 404 rather than a page dressed as a script.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(imagesRoot),
            RequestPath = "/api/images",
            // The photo set is content-stable; let the browser's HTTP cache keep it
            // for a day instead of re-fetching 50 JPEGs per session.
            OnPrepareResponse = ctx =>
                ctx.Context.Response.Headers.CacheControl = "public, max-age=86400",
        });

        #endregion static-files

        // #region routing
        // What: matches the request to an endpoint. How: an explicit UseRouting; left
        // out, ASP.NET Core adds one at the very top. Why here: after the files, which
        // never need a route, and before the user and the endpoint. The health probes
        // are marked ShortCircuit (HealthEndpoints.cs), so routing answers them right
        // here: an orchestrator polling every few seconds never reads a token, never
        // renews a session and never waits for a store. Anything
        // that needs the matched endpoint later (a CORS policy, a rate limit, an output
        // cache policy) goes below this line.
        app.UseRouting();
        // #endregion routing

        // #region user
        // What: the signed-in user, from the session cookie, and the rule check. Why
        // here: after routing, so authorization knows which endpoint it is guarding,
        // and before any endpoint, so a request carries its user by the time one runs.
        // The reads do not require it and still get a principal when a cookie is
        // present, which is how the listing knows whose badges to draw.
        app.UseAuthentication();
        app.UseAuthorization();
        // #endregion user

        // #region session-renewal
        // A signed-in request carries its session forward: once a day, the first
        // request that arrives with a token more than a day into its life gets a
        // fresh cookie with the same claims and a fresh year. Before the endpoint
        // runs, because a cookie has to be set before the response starts; only on
        // the API, where the token is read; never on the way out, where the cookie
        // is being deleted.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                && !context.Request.Path.StartsWithSegments("/api/auth/logout")
                && context.User.Identity?.IsAuthenticated == true
                && tokens.ShouldRenew(context.User.FindFirst("exp")?.Value, DateTimeOffset.UtcNow))
            {
                string? id = context.UserIdOrNull();
                string? email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                string? store = context.User.FindFirst(TokenIssuer.StoreClaim)?.Value;
                if (id is not null && email is not null && store is not null)
                {
                    context.Response.Cookies.Append(TokenIssuer.CookieName, tokens.Issue(id, email, store), TokenIssuer.CookieFor(context, tokens.Lifetime));
                }
            }

            await next();
        });
        // #endregion session-renewal

        // #region warm-before-reading
        // And before any endpoint reads a store, that store's catalogue is loaded, so
        // no request thread is ever blocked on a load in progress (Warmth, in
        // Stores.cs). Only the API: the page's files do not have a store.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await Warmth.EnsureAsync(context.RequestServices.GetRequiredService<CurrentBackend>().Backend);
            }
            await next();
        });
        // #endregion warm-before-reading
        return app;
    }
}
