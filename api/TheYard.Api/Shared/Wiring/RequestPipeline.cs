
namespace TheYard.Api.Shared.Wiring;

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
    /// <param name="app">The built host.</param>
    /// <param name="paths">Where the photographs are.</param>
    /// <param name="identity">Where this process runs: the web app's name goes on every answer.</param>
    public static WebApplication UseTheYardRequestPipeline(this WebApplication app, HostPaths paths, HostIdentity identity)
    {
        // What the steps below use, made at registration and the same for every request.
        var tokens = app.Services.GetRequiredService<TokenIssuer>();
        var errorLog = app.Services.GetRequiredService<ErrorRings>().Server;
        var recorder = app.Services.GetRequiredService<RequestRecorder>();
        var backends = app.Services.GetRequiredService<Backends>();
        string imagesRoot = paths.ImagesRoot;

        // How long the caller waited and what status they actually got (RequestRecording.cs).
        // Outermost, above the exception handler, because unwinding runs inner to outer.
        app.UseTheYardRequestTiming(recorder);

        // #region forwarded-headers
        // What: the visitor's address and scheme, read from the headers the edge and App
        // Service's front end add, and written onto the request. How: UseForwardedHeaders
        // with the options in ApiRegistration: X-Forwarded-For and X-Forwarded-Proto
        // only, counted back from the right as many hops as there are proxies we run
        // through, so for a visitor who comes through the edge, whatever they typed into
        // those headers is never read. A caller who skips the edge and calls the origin
        // directly can still write the entry that is read; the address feeds counts,
        // never a permission.
        // Why here: before everything that reads the address or the scheme on the way in
        // (the session cookie's Secure flag, the reset link), and the one reader outside
        // it, the timing above, reads the address on the way out, after this has written
        // it; the middleware does not put the old values back. Before this, each reader
        // read the raw header in its own way.
        //
        // The arrival read (/api/admin/arrival) shows the headers as they arrived, so
        // they are kept for that one address before the middleware consumes them.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(Arrival.Path, StringComparison.Ordinal))
            {
                context.Items[Arrival.RawKey] = new Arrival.Raw(
                    context.Request.Headers["X-Forwarded-For"].ToString(),
                    context.Request.Headers["X-Forwarded-Proto"].ToString());
            }
            await next();
        });
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

        // The record of every failure and the header that names the web app that answered
        // (RequestRecording.cs). Directly inside the exception handler, so the record sees a
        // failure in anything below it, signing in and renewing a session included.
        app.UseTheYardAnswerRecord(errorLog, identity);


        // What a browser and the edge may keep, what may be compressed, and the files that end a
        // request where they are found (ResponseKeeping.cs). Before routing, because none of the
        // three needs an endpoint and a file should not wait for one.
        app.UseTheYardResponseKeeping(imagesRoot);

        // #region routing
        // What: matches the request to an endpoint. How: an explicit UseRouting; left
        // out, ASP.NET Core adds one at the very top. Why here: after the files, which
        // never need a route, and before the user and the endpoint. The health probes
        // are marked ShortCircuit (HealthEndpoints.cs), so routing answers them right
        // here and they skip reading the session cookie. That is all it saves them: they
        // are not under /api, so the session's renewal and the store's warmth never
        // applied to them, and the timing ring already left them out by path. Anything
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
                string? stamp = context.User.FindFirst(TokenIssuer.StampClaim)?.Value;
                if (id is not null && email is not null && store is not null && stamp is not null)
                {
                    context.Response.Cookies.Append(TokenIssuer.CookieName, tokens.IssueWithFingerprint(id, email, store, stamp, tokens.Lifetime), TokenIssuer.CookieFor(context, tokens.Lifetime));
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
                await Warmth.EnsureAsync(backends.For(context), context.RequestAborted);
            }
            await next();
        });
        // #endregion warm-before-reading
        return app;
    }
}
