// The hosted sites stopped after the server has drained, so a request of theirs in flight at a
// roll finishes.
namespace TheYard.Api;

/// <summary>
/// Stops every hosted site when the host stops. Hosted services stop after the server has drained
/// its requests, and this one is registered last so it stops first among them: a Shed request still
/// running when the roll begins finishes before The Shed's services are disposed underneath it.
/// Stopping from the host's stopping signal instead ran before the drain, and blocked the whole
/// shutdown on a wait.
/// </summary>
public sealed class HostedSiteStopper(IEnumerable<IHostedSite> sites) : IHostedService
{
    /// <summary>Nothing to start: the sites are started once the host is built (HostedSiteMount.cs).</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stops and releases every site, awaited.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var site in sites)
        {
            await site.DisposeAsync();
        }
    }
}
