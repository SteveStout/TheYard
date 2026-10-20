namespace TheYard.Api;

/// <summary>
/// The sites this host runs inside its process beside the Yard's own (Sites/HostedSiteMount.cs):
/// The Shed today, and the one line this host adds to The Shed's own services. The Shed's
/// registrations are its own, in samples/theshed/Composition.
/// </summary>
public static class HostedSiteRegistration
{
    /// <summary>
    /// Registers the sites this process hosts and the service that stops them. The Shed is one,
    /// registered when the site list names a guest site: its folder, what this host adds to its
    /// builder, and the site itself. Called last, because hosted services stop in the reverse of
    /// their order and after the server has drained: a hosted site is stopped once no request of
    /// its own is in flight, and before anything it could need.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheYardHostedSites(this WebApplicationBuilder builder)
    {
        if (SiteSettings.From(builder.Configuration).Guests.FirstOrDefault() is { } shedHost)
        {
            builder.Services.AddSingleton(new ShedSettings(builder.Configuration["Shed:ContentRoot"]));
            builder.Services.AddSingleton(new ShedBuilderAddition(AddTheShedHostedByTheYard));
            builder.Services.AddSingleton<IHostedSite>(services => new ShedSite(
                shedHost,
                services.GetRequiredService<ShedSettings>(),
                services.GetServices<ShedBuilderAddition>(),
                services.GetRequiredService<IWebHostEnvironment>()));
        }
        builder.Services.AddHostedService<HostedSiteStopper>();
    }

    /// <summary>
    /// The process's signals (Ctrl+C, the container's stop) belong to this host, which stops The
    /// Shed on its way out, so The Shed gets a lifetime that answers none; a second console
    /// lifetime would answer them twice.
    /// </summary>
    /// <param name="shed">The Shed's own builder.</param>
    public static void AddTheShedHostedByTheYard(this WebApplicationBuilder shed) =>
        shed.Services.AddSingleton<IHostLifetime, HostedByAnotherApplication>();
}

/// <summary>Where The Shed's published files are (Shed:ContentRoot), or null where none is configured.</summary>
/// <param name="ContentRoot">The folder, or null.</param>
public sealed record ShedSettings(string? ContentRoot);

/// <summary>One thing this host adds to The Shed's builder before The Shed's own registrations run.</summary>
/// <param name="Apply">The addition.</param>
public sealed record ShedBuilderAddition(Action<WebApplicationBuilder> Apply);
