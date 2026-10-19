// The Shed's application inside this process, and the server with no socket it runs on, which
// Composition/ShedMount.cs starts and hands The Shed's requests to (ADR: One web app for every
// .NET process).
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using TestProject.Composition;

namespace TheYard.Api;

// #region shed-application
/// <summary>
/// The Shed's application, built the way samples/theshed/Program.cs builds it, held in this
/// process with the in-process server standing where Kestrel would.
/// </summary>
public sealed class ShedApplication : IDisposable
{
    private readonly WebApplication _app;
    private readonly InProcessServer _server;
    private readonly long? _maxRequestBytes;

    private ShedApplication(WebApplication app, InProcessServer server, long? maxRequestBytes)
    {
        _app = app;
        _server = server;
        _maxRequestBytes = maxRequestBytes;
    }

    /// <summary>
    /// Builds and starts The Shed from its published folder: the same four registrations, the
    /// same pipeline and the same controllers as its Program.cs, in that order.
    /// </summary>
    public static async Task<ShedApplication> StartAsync(string contentRoot, string environmentName, CancellationToken stopping)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // The controllers are found in the application's own assembly, by this name.
            ApplicationName = typeof(TestProject.Program).Assembly.GetName().Name,
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot"),
            EnvironmentName = environmentName,
        });
        var server = new InProcessServer();
        builder.WebHost.UseServer(server);
        builder.AddTheShedFiles();                    // samples/theshed/Composition/FilesRegistration.cs
        builder.AddTheShedDocumentationAndVersion();  // samples/theshed/Composition/DocumentationAndVersionRegistration.cs
        builder.AddTheShedApi();                      // samples/theshed/Composition/ApiRegistration.cs
        builder.AddTheShedWriteLimits();              // samples/theshed/Composition/WriteLimitRegistration.cs
        var app = builder.Build();
        app.UseTheShedRequestPipeline();              // samples/theshed/Composition/RequestPipeline.cs
        app.MapControllers();                         // samples/theshed/Controllers
        await app.StartAsync(stopping);

        // The Shed asks its Kestrel for its upload limit plus a megabyte of form overhead; that
        // Kestrel never listens here, so the same number is set on each Shed request instead.
        long? maxUpload = builder.Configuration.GetSection("Files").GetValue<long?>("MaxUploadBytes");
        return new ShedApplication(app, server, maxUpload is { } bytes ? bytes + (1024 * 1024) : null);
    }

    /// <summary>
    /// Hands one request to The Shed: the same connection and the same request and response,
    /// seen through a context of The Shed's own with its own request services.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        if (_maxRequestBytes is { } limit
            && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }
        // A layer over the connection's features. The request services are The Shed's: the
        // connection's own feature collection already holds TheYard's, which would otherwise be
        // the ones The Shed's controllers were resolved from.
        await using var scope = _app.Services.CreateAsyncScope();
        var features = new FeatureCollection(context.Features);
        features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
        await _server.HandleAsync(features);
    }

    /// <summary>Stops The Shed's application with the process.</summary>
    public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>
/// A server with no socket: what stands in for Kestrel in an application that is handed its
/// requests by another one in the same process. ASP.NET Core's hosting layer starts it with the
/// application's request handler, which is all it keeps.
/// </summary>
public sealed class InProcessServer : IServer
{
    private Func<IFeatureCollection, Task>? _handle;

    /// <summary>The server's own features: none, as nothing listens.</summary>
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <summary>Keeps the application's handler; there is nothing to bind.</summary>
    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull
    {
        _handle = async features =>
        {
            var context = application.CreateContext(features);
            try
            {
                await application.ProcessRequestAsync(context);
                application.DisposeContext(context, null);
            }
            catch (Exception ex)
            {
                application.DisposeContext(context, ex);
                throw;
            }
        };
        return Task.CompletedTask;
    }

    /// <summary>Runs one request through the application.</summary>
    public Task HandleAsync(IFeatureCollection features) =>
        _handle?.Invoke(features) ?? throw new InvalidOperationException("The Shed's application has not started");

    /// <summary>Nothing to stop.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Nothing to release.</summary>
    public void Dispose()
    {
    }
}
// #endregion shed-application
