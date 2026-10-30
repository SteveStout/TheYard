// What a browser and the edge may keep, what may be compressed, and the files that end a request
// where they are found. Its own file because these three answer one question between them, how long
// an answer is good for and in what shape it goes out, which is a different job from recording an
// answer (RequestRecording.cs) and from the order of the pipeline itself (RequestPipeline.cs).
using Microsoft.Extensions.FileProviders;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// The cache rules, the one compressed branch and the static files, in the order they have to be
/// in: the rules are registered before anything that writes a response so they wrap it, and the
/// files sit ahead of routing because none of them needs an endpoint (ADR: The order of the request
/// pipeline, and ADR-015).
/// </summary>
public static class ResponseKeeping
{
    /// <summary>Adds the cache rules, the catalogue's compressed branch and the files.</summary>
    /// <param name="app">The built host.</param>
    /// <param name="imagesRoot">Where the photographs are, served under /api/images.</param>
    public static WebApplication UseTheYardResponseKeeping(this WebApplication app, string imagesRoot)
    {
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
                        : EdgeKeeps.BrowserRuleFor(context) ?? "no-cache";
                }
                // The catalogue's two reads may be kept by the edge for a few seconds, and what
                // only a deploy changes for a day (EdgeKeeps); the browser's rule above is unchanged.
                CatalogueReads.LetTheEdgeKeep(context);
                EdgeKeeps.LetTheEdgeKeep(context);
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

        return app;
    }
}
