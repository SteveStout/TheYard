namespace TheYard.Api;

/// <summary>
/// What this host adds to The Shed's own services when it runs The Shed inside its process
/// (ShedMount.cs). The Shed's registrations are its own, in samples/theshed/Composition; this is
/// the one line that belongs to the host that owns the process.
/// </summary>
public static class ShedRegistration
{
    /// <summary>
    /// The process's signals (Ctrl+C, the container's stop) belong to this host, which stops The
    /// Shed on its way out, so The Shed gets a lifetime that answers none; a second console
    /// lifetime would answer them twice.
    /// </summary>
    public static void AddTheShedHostedByTheYard(this WebApplicationBuilder shed) =>
        shed.Services.AddSingleton<IHostLifetime, HostedByAnotherApplication>();
}
