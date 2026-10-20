// The Shed stopped after the server has drained, so a Shed request in flight at a roll finishes.
namespace TheYard.Api;

/// <summary>
/// Stops The Shed when the host stops. Hosted services stop after the server has drained its
/// requests, and this one is registered last so it stops first among them: a Shed request still
/// running when the roll begins finishes before The Shed's services are disposed underneath it.
/// Stopping from the host's stopping signal instead ran before the drain, and blocked the whole
/// shutdown on a wait.
/// </summary>
public sealed class ShedStopper(ShedSlot slot) : IHostedService
{
    /// <summary>Nothing to start: The Shed is mounted once the host is built.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stops and releases The Shed, awaited.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => slot.ReleaseAsync().AsTask();
}
