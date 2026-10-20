namespace TheYard.Api;

// #region hosted-sites
/// <summary>
/// The sites this process hosts beside the Yard's own (IHostedSite), started once the host is built
/// and handed their requests first. A request whose name is a hosted site's is handed to that site
/// whole, before anything of TheYard's pipeline sees it; every other request carries on to TheYard.
/// A site that cannot start must not take the Yard down with it: its name answers 503 with a
/// sentence, the reason goes to the log, and the Yard names carry on.
/// </summary>
public static class HostedSiteMount
{
    /// <summary>Starts every hosted site and adds the one middleware that hands each its requests.</summary>
    /// <param name="app">The built host.</param>
    public static async Task MountTheHostedSitesAsync(this WebApplication app)
    {
        var sites = app.Services.GetServices<IHostedSite>().ToList();
        if (sites.Count == 0)
        {
            return;
        }
        var hosts = app.Services.GetRequiredService<SiteHosts>();
        var errorLog = app.Services.GetRequiredService<ErrorRings>().Server;
        foreach (var site in sites)
        {
            try
            {
                await site.StartAsync(app.Lifetime.ApplicationStopping);
                app.Logger.LogInformation("The hosted site {Host} is running", site.Host);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                app.Logger.LogError(ex, "The hosted site {Host} did not start ({Reason}); its name answers 503", site.Host, ex.GetType().Name);
            }
        }

        app.Use(async (context, next) =>
        {
            if (hosts.GuestOf(context) is not { } guest || sites.Find(site => site.Host == guest) is not { } site)
            {
                await next();
                return;
            }
            if (!site.Running)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync($"The site {site.Host} is not running in this process; the log says why.", context.RequestAborted);
                return;
            }
            try
            {
                await site.HandleAsync(context);
            }
            catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
            {
                // The site's own handler answers its errors; anything past it is logged here, in
                // TheYard's log and TheYard's error record, and answered as a problem rather than
                // a bare 500. The record takes the type and the frames, never the message, as the
                // error record does for TheYard's own requests.
                app.Logger.LogError(ex, "The hosted site {Host} failed a request to {Path}", site.Host, context.Request.Path);
                errorLog.Record(context.Request.Path, StatusCodes.Status500InternalServerError, ex.GetType().Name, StackFrames.Of(ex));
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { title = $"{site.Host} could not answer", status = 500 }, context.RequestAborted);
                }
            }
        });
    }
}
// #endregion hosted-sites
