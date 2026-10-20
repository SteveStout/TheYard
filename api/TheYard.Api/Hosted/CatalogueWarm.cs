// The default store's bids and catalogue, loaded beside the server rather than before it, and
// the other stores warmed after it where the configuration asks.
namespace TheYard.Api;

/// <summary>
/// Starts the default store's load the moment the host starts and returns at once, so the server
/// listens while the catalogue loads; on a deployed web app, the other stores' loads follow
/// (Store:WarmOthers).
/// </summary>
public sealed class CatalogueWarm(Backends backends, IHostApplicationLifetime lifetime, ILogger<CatalogueWarm> logger, bool warmOthers) : IHostedService
{
    /// <summary>Starts the loads beside the server; nothing is awaited here.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // #region listen-first
        // The default store's bids and catalogue load beside the server, not before
        // it. The server listens at once, so a roll's new container answers the
        // platform's probes and serves the page's files in seconds. Every API read
        // waits for the catalogue, and waits without holding a thread, because the
        // pipeline awaits the warm before any /api endpoint runs (Warmth, Stores.cs).
        // /readyz says not ready until the catalogue is in, so the deploy and a
        // blue-green swap wait for the same thing a visitor would.
        //
        // The bids replay first. It is a few rows against a hundred thousand
        // vehicles, so it is done long before the catalogue, and an API read let
        // through by the catalogue's warm finds every bid already in place: no
        // visitor sees a sold vehicle offered, or their own bids missing, in the
        // first minutes after a roll.
        //
        // A dataset that cannot be loaded still ends the process, loudly, rather
        // than serving a site with no catalogue: the failure is logged and the host
        // is stopped, so the platform restarts it and says so. A host already
        // stopping, as a test's host does when it is disposed mid-load, is left to stop.
        _ = Task.Run(async () =>
        {
            try
            {
                await backends.Default.Startup.Time("bids", backends.Default.Bids.LoadAsync);
                await backends.Default.Startup.Time("catalogue", backends.Default.Inventory.WarmAsync);
                backends.Default.Startup.Ready();
            }
            catch (Exception ex) when (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                logger.LogCritical(ex, "The {Store} store could not be loaded; stopping, so the platform restarts the container", backends.Default.Name);
                lifetime.StopApplication();
            }
        }, CancellationToken.None);
        // #endregion listen-first
        if (warmOthers)
        {
            _ = Task.Run(WarmTheOthersAsync, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop: a load in flight ends with the process.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmTheOthersAsync()
    {
        foreach (var backend in backends.All.Where(candidate => !ReferenceEquals(candidate, backends.Default)))
        {
            try
            {
                await backend.Startup.Time("catalogue", backend.Inventory.WarmAsync);
                await backend.Startup.Time("bids", backend.Bids.LoadAsync);
                backend.Startup.Ready();
            }
            catch (Exception ex)
            {
                // The type only; the message can carry a host name and this line reaches the
                // public log section.
                logger.LogError("Warming the {Store} store failed with {Exception}; its first visitor will try again", backend.Name, ex.GetType().Name);
            }
        }
    }
}
