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
    /// set one, adds nothing. The started Shed is kept in the ShedSlot, and stopped from there
    /// after the server has drained (Shared/Hosted/ShedStopper.cs).
    /// </summary>
    /// <param name="app">The built host.</param>
    public static async Task MountTheShedAsync(this WebApplication app)
    {
        var hosts = app.Services.GetRequiredService<SiteHosts>();
        string? contentRoot = app.Services.GetRequiredService<ShedSettings>().ContentRoot;
        if (hosts.Shed is null)
        {
            return;
        }
        var slot = app.Services.GetRequiredService<ShedSlot>();
        var errorLog = app.Services.GetRequiredService<ErrorRings>().Server;
        var additions = app.Services.GetServices<ShedBuilderAddition>().ToList();

        // A Shed that cannot start must not take the two Yard sites down with it: its name answers
        // 503 with a sentence, the reason goes to the log, and the Yard names carry on.
        string? failure = null;
        if (string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot))
        {
            failure = $"Shed:ContentRoot {contentRoot} is not a folder";
        }
        else
        {
            try
            {
                slot.Hold(await ShedApplication.StartAsync(
                    contentRoot,
                    app.Environment.EnvironmentName,
                    shed => additions.ForEach(addition => addition.Apply(shed)),   // Shared/ShedRegistration.cs
                    app.Lifetime.ApplicationStopping));
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
            if (slot.Shed is not { } shed)
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
                // TheYard's log and TheYard's error record, and answered as a problem rather than
                // a bare 500. The record takes the type and the frames, never the message, as the
                // error record does for TheYard's own requests.
                app.Logger.LogError(ex, "The Shed failed a request to {Path}", context.Request.Path);
                errorLog.Record(context.Request.Path, StatusCodes.Status500InternalServerError, ex.GetType().Name, StackFrames.Of(ex));
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { title = "The Shed could not answer", status = 500 }, context.RequestAborted);
                }
            }
        });
        if (slot.Shed is not null)
        {
            app.Logger.LogInformation("The Shed is mounted on {Host} from {Root}", hosts.Shed, contentRoot);
        }
    }
}

// #endregion shed-mount
