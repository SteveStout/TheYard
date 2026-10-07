using Microsoft.AspNetCore.HttpOverrides;

namespace TestProject.Composition;

/// <summary>
/// Sets up the request pipeline: the middleware every request passes through in order, and
/// every response passes back through in reverse. The scheme App Service's front end forwards
/// comes first, then error handling, so a failure in anything after it becomes a problem
/// document, then HSTS in production and the HTTPS redirect for local runs, then the static
/// files for the page, then routing. Requests that none of these answer go to the controllers.
/// </summary>
public static class RequestPipeline
{
    /// <summary>Adds the app's middleware in the order it must run.</summary>
    /// <param name="app">The app being built.</param>
    public static void UseTheShedRequestPipeline(this WebApplication app)
    {
        // #region request-pipeline
        // This site's domain is bound to App Service itself: no edge stands in front of it,
        // which the live headers show (Server: Kestrel at the domain). App Service's front end
        // terminates TLS and speaks plain HTTP to the container, and says the visitor's scheme
        // in X-Forwarded-Proto, one hop. Only the scheme is read: nothing here uses the
        // visitor's address. First, so HSTS below knows the request was HTTPS.
        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        app.UseForwardedHeaders(forwarded);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        // With no edge to send it, the app sends HSTS itself: a browser that has been here over
        // HTTPS will not try plain HTTP again for thirty days. Not in Development, where a
        // developer's localhost would remember it.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }
        // The HTTPS redirect runs only in Development. In production the hosting platform handles
        // TLS in front of the app, so the app sees every request as plain HTTP. A redirect here
        // would treat those requests as insecure even when the visitor already used HTTPS.
        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // Routing after the files, which never need a route, as in TheYard; said out loud
        // rather than left for ASP.NET Core to put at the top, so anything that needs the
        // matched endpoint has a line to go below.
        app.UseRouting();
        // What this site leaves out of TheYard's pipeline, and why. No visitor address from the
        // forwarded headers: nothing here counts visitors, sets a cookie or builds a link from a
        // host. No compression: the page is about 4.5 KB and its script about 1 KB, measured at
        // the domain on 7 October, so there is little to save and the core is shared. No cache
        // rules: the files keep plain names (css/, js/), so a long cache would serve a stale file
        // after a deploy; the static files middleware's own ETag and Last-Modified let a browser
        // ask cheaply instead. No short-circuited probes: there is no user, session or store
        // between routing and the health controller to skip (TheYard: ADR: The order of the
        // request pipeline).
        // #endregion request-pipeline
    }
}
