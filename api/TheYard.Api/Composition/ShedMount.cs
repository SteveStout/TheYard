namespace TheYard.Api;

// #region shed-mount
/// <summary>
/// The Shed, served by this process under its own name (ADR: One web app for every .NET
/// process). The Shed is a whole ASP.NET Core application of its own, the getting-started
/// sample under samples/theshed, and its Program.cs stays the way a reader would start it on
/// their own machine. ShedApplication.cs starts the same application inside this process: the
/// Shed's own builder, its own four registrations, its own request pipeline and controllers, in
/// a service container of its own, with an in-process server where Kestrel would be. A request
/// whose name is The Shed's is handed to that application whole, before anything of TheYard's
/// pipeline sees it; every other request carries on to TheYard.
///
/// <para>Why a second application rather than the Shed's services on TheYard's builder: every
/// collision two applications on one builder would have is gone by construction. The Shed's Kestrel body limit
/// configures a server that never listens, so TheYard's limit is untouched, and the Shed's own
/// limit is set on each Shed request below. Its rate limiter, exception handler, problem
/// details and snake_case JSON live in its own container, so none of them reaches TheYard's
/// endpoints and no policy name can clash. Its /healthz is its own, reached only on its name.
/// Its documents, sample home and live sources are read from its own content root.</para>
/// </summary>
public static class ShedMount
{
    /// <summary>
    /// Starts The Shed in this process when the deploy names its host (Hosts:Shed) and where its
    /// published files are (Shed:ContentRoot), and adds the one middleware that hands it its
    /// requests. Neither set, which is every test host and a developer's machine, adds nothing.
    /// </summary>
    public static async Task MountTheShedAsync(this WebApplication app)
    {
        var hosts = app.Services.GetRequiredService<SiteHosts>();
        string? contentRoot = app.Configuration["Shed:ContentRoot"];
        if (hosts.Shed is null || string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot))
        {
            if (hosts.Shed is not null)
            {
                app.Logger.LogWarning("Hosts:Shed is {Host} but Shed:ContentRoot {Root} is not a folder; The Shed is not mounted", hosts.Shed, contentRoot);
            }
            return;
        }

        var shed = await ShedApplication.StartAsync(contentRoot, app.Environment.EnvironmentName, app.Lifetime.ApplicationStopping);
        app.Lifetime.ApplicationStopped.Register(() => shed.Dispose());
        app.Use(async (context, next) =>
        {
            if (hosts.IsShed(context))
            {
                await shed.HandleAsync(context);
                return;
            }
            await next();
        });
        app.Logger.LogInformation("The Shed is mounted on {Host} from {Root}", hosts.Shed, contentRoot);
    }
}

// #endregion shed-mount
