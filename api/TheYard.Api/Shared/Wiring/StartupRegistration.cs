namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// What runs when the host starts, one hosted service per concern, in the order they start: the
/// stores said out loud, the scratch database's cleanup, the error rings and the start time, the
/// default catalogue's warm beside the server, the page sweep at the roll and, where it is on,
/// the keep-warm loop. Hosted services start before the server listens and stop after it has
/// drained, in the reverse of this order.
/// </summary>
public static class StartupRegistration
{
    /// <summary>Registers the startup services.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="identity">Where this process runs: the keep-warm loop is on by default on App Service only.</param>
    public static void AddTheYardStartup(this WebApplicationBuilder builder, HostIdentity identity)
    {
        builder.Services.AddHostedService<StoreAnnouncement>();
        builder.Services.AddHostedService<ScratchDatabaseCleanup>();
        builder.Services.AddHostedService<ErrorRingKeeping>();
        bool warmOthers = builder.Configuration.GetValue("Store:WarmOthers", false);
        builder.Services.AddHostedService(services => new CatalogueWarm(
            services.GetRequiredService<Backends>(),
            services.GetRequiredService<IHostApplicationLifetime>(),
            services.GetRequiredService<ILogger<CatalogueWarm>>(),
            warmOthers));
        builder.Services.AddHostedService<PageSweepAtRoll>();
        // On where App Service runs the site and off everywhere else, the test host included;
        // KeepWarm:Enabled says otherwise either way (ADR: Kept awake).
        if (builder.Configuration.GetValue("KeepWarm:Enabled", identity.SiteName is not null))
        {
            builder.Services.AddHostedService<KeepWarmAtRoll>();
        }
    }
}
