// The Shed, as a site this process hosts: its published folder, its own application built the
// way its Program.cs builds it, and what this host adds to that application's builder.
namespace TheYard.Api.Shared.Sites;

/// <summary>
/// The Shed (ADR: One web app for every .NET process): the getting-started sample under
/// samples/theshed, a whole ASP.NET Core application of its own, started inside this process from
/// its published folder (Shed:ContentRoot) and answering its own name. Every collision two
/// applications in one service container would have is gone by construction (ShedApplication.cs).
/// This is the one type, with ShedApplication, that may use The Shed's assembly.
/// </summary>
public sealed class ShedSite(string host, ShedSettings settings, IEnumerable<ShedBuilderAddition> additions, IWebHostEnvironment environment) : IHostedSite
{
    private ShedApplication? _application;

    /// <summary>The Shed's public name.</summary>
    public string Host { get; } = host;

    /// <summary>True once The Shed has started and until it is disposed.</summary>
    public bool Running => Volatile.Read(ref _application) is not null;

    /// <summary>Starts The Shed from its folder; a folder that is not there is a start that failed.</summary>
    /// <param name="stopping">Cancelled when the host begins to stop.</param>
    public async Task StartAsync(CancellationToken stopping)
    {
        if (string.IsNullOrWhiteSpace(settings.ContentRoot) || !Directory.Exists(settings.ContentRoot))
        {
            throw new DirectoryNotFoundException($"Shed:ContentRoot {settings.ContentRoot} is not a folder");
        }
        var steps = additions.ToList();
        Volatile.Write(ref _application, await ShedApplication.StartAsync(
            settings.ContentRoot,
            environment.EnvironmentName,
            shed => steps.ForEach(addition => addition.Apply(shed)),   // Shared/Wiring/HostedSiteRegistration.cs
            stopping));
    }

    /// <summary>Hands one request to The Shed's own application.</summary>
    /// <param name="context">The request.</param>
    public Task HandleAsync(HttpContext context) =>
        (Volatile.Read(ref _application) ?? throw new InvalidOperationException("The Shed has not started")).HandleAsync(context);

    /// <summary>Stops and releases The Shed, once.</summary>
    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _application, null) is { } application ? application.DisposeAsync() : ValueTask.CompletedTask;
}
