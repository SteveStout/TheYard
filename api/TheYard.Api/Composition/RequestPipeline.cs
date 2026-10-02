using System.Diagnostics;
using Microsoft.Extensions.FileProviders;

namespace TheYard.Api;

/// <summary>
/// The request pipeline, in ASP.NET Core's own term: the middleware every request passes
/// through in this order, and every response passes back through in reverse, with the
/// reason beside each piece: timing outermost, then the problem shape, logging, the user,
/// the session's renewal, the store's warmth, the error record, the cache rules and the
/// files last.
/// </summary>
public static class RequestPipeline
{
    /// <summary>Adds TheYard's middleware in its required order.</summary>
    public static WebApplication UseTheYardRequestPipeline(this WebApplication app, YardComposition host)
    {
        var tokens = host.Tokens;
        var errorLog = host.ErrorRings.Server;
        string imagesRoot = host.Paths.ImagesRoot;
        // First in the pipeline, because it can only catch what is registered after
        // it: an unhandled exception becomes a 500 ProblemDetails instead of an empty
        // body (ADR-023), filled in by ProblemHandler (ADR-030). The request logger
        // sits behind it so a failed request is still logged with its real status.
        #region request-timing
        // Timing, and it has to be the outermost thing here.
        //
        // The first version of this sat further down the pipeline, below
        // UseExceptionHandler, and its comment claimed it measured "the whole cost a
        // caller waited for, including the time spent turning an exception into a
        // ProblemDetails". Both halves were false. Unwinding runs inner to outer, so a
        // request that threw reached this finally before the handler had written
        // anything, and every failed request was recorded as a 200 with the handler's
        // time excluded. /api/admin/selftest/exception answers 500 to its caller and
        // was appearing in the metrics as 200 (the staff review, 2026-09-03).
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

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseHttpLogging();

        // Before any endpoint, so a request carries its user by the time one runs. The
        // reads do not require it and still get a principal when a cookie is present,
        // which is how the listing knows whose badges to draw.
        app.UseAuthentication();
        app.UseAuthorization();

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

        #region error-log
        // Middleware, so it sees every response including the ones no endpoint
        // returned. It records and rethrows rather than handling: the ProblemDetails
        // handler registered earlier owns the response, this only owns the record
        // (ADR-010, ADR-023).
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
        // Cache rules (ADR-015), from the shape of the address. Vite names every
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
                return Task.CompletedTask;
            });
            await next();
        });
        #endregion cache-headers

        #region static-files
        // Registered last on purpose. Middleware runs in registration order, so the
        // cache rules above must already be in place, and the SPA fallback must be
        // the last word: it answers app routes with index.html, while an address that
        // looks like a file stays a 404 rather than a page dressed as a script.
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
        return app;
    }
}
