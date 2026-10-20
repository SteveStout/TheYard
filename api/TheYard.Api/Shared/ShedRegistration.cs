namespace TheYard.Api;

/// <summary>
/// What this host registers for The Shed it runs inside its process (ShedMount.cs), and the one
/// line it adds to The Shed's own services. The Shed's registrations are its own, in
/// samples/theshed/Composition.
/// </summary>
public static class ShedRegistration
{
    /// <summary>
    /// Registers where The Shed's published files are, the slot the started Shed is kept in, what
    /// this host adds to The Shed's builder, and the service that stops it. Called last, because
    /// hosted services stop in the reverse of their order and after the server has drained: The
    /// Shed is stopped once no request of its own is in flight, and before anything it could need.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheYardShed(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new ShedSettings(builder.Configuration["Shed:ContentRoot"]));
        builder.Services.AddSingleton<ShedSlot>();
        builder.Services.AddSingleton(new ShedBuilderAddition(AddTheShedHostedByTheYard));
        builder.Services.AddHostedService<ShedStopper>();
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
