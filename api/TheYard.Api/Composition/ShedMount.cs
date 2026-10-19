namespace TheYard.Api;

// #region shed-mount
/// <summary>
/// The Shed, served by this process under its own name (ADR: One web app for every .NET
/// process). The Shed is a whole ASP.NET Core application of its own, the getting-started
/// sample under samples/theshed, and its Program.cs stays the way a reader would start it on
/// their own machine. ShedApplication.cs starts the same application inside this process: the
/// Shed's own builder, its own registrations, its own request pipeline and controllers, in
/// a service container of its own, with an in-process server where Kestrel would be. A request
/// whose name is The Shed's is handed to that application whole, before anything of TheYard's
/// pipeline sees it; every other request carries on to TheYard.
///
/// <para>Why a second application rather than the Shed's services on TheYard's builder: every
/// collision two applications in one service container would have is gone by construction. The Shed's Kestrel body limit
/// configures a server that never listens, so TheYard's limit is untouched, and the Shed's own
/// limit is set on each Shed request below. Its rate limiter, exception handler, problem
/// details and snake_case JSON live in its own container, so none of them reaches TheYard's
/// endpoints and no policy name can clash. Its /healthz is its own, reached only on its name.
/// Its documents, sample home and live sources are read from its own content root. What the two
/// still share is the process: its memory limit, its environment variables (each reads only its
/// own settings) and the framework's process-wide telemetry sources.</para>
/// </summary>
public static class ShedMount
{
    /// <summary>
    /// Starts The Shed in this process when the deploy names its host (Hosts:Shed) and where its
    /// published files are (Shed:ContentRoot), and adds the one middleware that hands it its
    /// requests. No host named, which is a developer's machine and every test host that does not
    /// set one, adds nothing.
    /// </summary>
    public static async Task MountTheShedAsync(this WebApplication app)
    {
        var hosts = app.Services.GetRequiredService<SiteHosts>();
        string? contentRoot = app.Configuration["Shed:ContentRoot"];
        if (hosts.Shed is null)
        {
            return;
        }

        // A Shed that cannot start must not take the two Yard sites down with it: its name answers
        // 503 with a sentence, the reason goes to the log, and the Yard names carry on.
        ShedApplication? shed = null;
        string? failure = null;
        if (string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot))
        {
            failure = $"Shed:ContentRoot {contentRoot} is not a folder";
        }
        else
        {
            try
            {
                var started = await ShedApplication.StartAsync(
                    contentRoot,
                    app.Environment.EnvironmentName,
                    ShedRegistration.AddTheShedHostedByTheYard,   // Composition/ShedRegistration.cs
                    app.Lifetime.ApplicationStopping);
                app.Lifetime.ApplicationStopping.Register(() => started.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10)));
                shed = started;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = $"The Shed did not start ({ex.GetType().Name})";
                app.Logger.LogError(ex, "The Shed did not start; its name answers 503");
            }
        }
        if (failure is not null)
        {
            app.Logger.LogWarning("The Shed is not mounted on {Host}: {Reason}", hosts.Shed, failure);
        }

        app.Use(async (context, next) =>
        {
            if (!hosts.IsShed(context))
            {
                await next();
                return;
            }
            if (shed is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("The Shed is not running in this process; the log says why.", context.RequestAborted);
                return;
            }
            try
            {
                await shed.HandleAsync(context);
            }
            catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
            {
                // The Shed's own handler answers its errors; anything past it is logged here, in
                // TheYard's log, and answered as a problem rather than a bare 500.
                app.Logger.LogError(ex, "The Shed failed a request to {Path}", context.Request.Path);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { title = "The Shed could not answer", status = 500 }, context.RequestAborted);
                }
            }
        });
        if (shed is not null)
        {
            app.Logger.LogInformation("The Shed is mounted on {Host} from {Root}", hosts.Shed, contentRoot);
        }
    }
}

// #endregion shed-mount
