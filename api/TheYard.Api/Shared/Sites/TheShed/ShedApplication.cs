// The Shed's application inside this process, and the server with no socket it runs on, which
// Shared/ShedMount.cs starts and hands The Shed's requests to (ADR: One web app for every
// .NET process).
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using TestProject.Composition;

namespace TheYard.Api;

// #region shed-application
/// <summary>
/// The Shed's application, built the way samples/theshed/Program.cs builds it, held in this
/// process with the in-process server standing where Kestrel would.
/// </summary>
public sealed class ShedApplication : IAsyncDisposable
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
    /// Builds and starts The Shed from its published folder: the same registrations (files,
    /// documents and version, the API, the write limits), the same pipeline and the same
    /// controllers as its Program.cs, in that order, with what the owning host adds.
    /// </summary>
    public static async Task<ShedApplication> StartAsync(string contentRoot, string environmentName, Action<WebApplicationBuilder> hosted, CancellationToken stopping)
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
        // What the host that owns the process adds to The Shed's services (Shared/ShedMount.cs).
        hosted(builder);
        builder.AddTheShedFiles();                    // samples/theshed/Composition/FilesRegistration.cs
        builder.AddTheShedDocumentationAndVersion();  // samples/theshed/Composition/DocumentationAndVersionRegistration.cs
        builder.AddTheShedApi();                      // samples/theshed/Composition/ApiRegistration.cs
        builder.AddTheShedWriteLimits();              // samples/theshed/Composition/WriteLimitRegistration.cs
        var app = builder.Build();
        app.UseTheShedRequestPipeline();              // samples/theshed/Composition/RequestPipeline.cs
        app.MapControllers();                         // samples/theshed/Controllers
        await app.StartAsync(stopping);

        // The Shed sizes its form reader from its upload limit (FilesRegistration.cs) and asks its
        // Kestrel for the same number; that Kestrel never listens here, so the form reader's
        // limit, as The Shed resolved it, is set on each of its requests instead.
        long? limit = app.Services.GetRequiredService<IOptions<FormOptions>>().Value.MultipartBodyLengthLimit;
        return new ShedApplication(app, server, limit);
    }

    /// <summary>
    /// Hands one request to The Shed: the same connection and the same request and response,
    /// seen through a context of The Shed's own with its own request services. The context and
    /// the request's service scope are released when the response has finished, as Kestrel would
    /// release them, so anything The Shed asked to run on completion still has its services.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        if (_maxRequestBytes is { } limit
            && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }
        // A layer over the connection's features with The Shed's request services in it. The
        // connection's own collection may hold TheYard's, if anything ahead of this asked for
        // them, and those are not the ones The Shed's controllers are resolved from.
        var scope = _app.Services.CreateAsyncScope();
        context.Response.RegisterForDisposeAsync(scope);
        var features = new FeatureCollection(context.Features);
        features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
        var finished = await _server.HandleAsync(features);
        context.Response.RegisterForDisposeAsync(finished);
    }

    /// <summary>Stops The Shed's application, its hosted services first, then releases it.</summary>
    public async ValueTask DisposeAsync()
    {
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _app.StopAsync(patience.Token);
        await _app.DisposeAsync();
    }
}

/// <summary>
/// A server with no socket: what stands in for Kestrel in an application that is handed its
/// requests by another one in the same process. ASP.NET Core's hosting layer starts it with the
/// application's request handler, which is all it keeps.
/// </summary>
public sealed class InProcessServer : IServer
{
    private Func<IFeatureCollection, Task<IAsyncDisposable>>? _handle;

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
            }
            catch (Exception ex)
            {
                application.DisposeContext(context, ex);
                throw;
            }
            return new Finished(() => application.DisposeContext(context, null));
        };
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs one request through the application, and answers what to call when the response has
    /// finished: the hosting layer's end of the request (its log line, its activity, its scope).
    /// </summary>
    public Task<IAsyncDisposable> HandleAsync(IFeatureCollection features) =>
        _handle?.Invoke(features) ?? throw new InvalidOperationException("The Shed's application has not started");

    /// <summary>Nothing to stop.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Nothing to release.</summary>
    public void Dispose()
    {
    }

    /// <summary>The end of one request, run once the outer response has finished.</summary>
    private sealed class Finished(Action end) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            end();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// The lifetime of an application whose process belongs to another: it waits for nothing and
/// answers no signal, because the host that owns the process stops this one itself.
/// </summary>
public sealed class HostedByAnotherApplication : IHostLifetime
{
    /// <summary>Nothing to wait for before starting.</summary>
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Nothing to undo when stopping.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
// #endregion shed-application
