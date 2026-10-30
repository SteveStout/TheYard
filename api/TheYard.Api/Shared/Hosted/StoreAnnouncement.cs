// What the host does about its stores the moment it starts, in two moves: attach the loggers,
// then say what came up. The two moves are types of their own in this folder,
// StoreLogAttachment.cs and StoreStartupLines.cs; this file holds the order and nothing else.

namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Attaches the host's loggers to the stores and says what came up, in that order, because a line
/// written before the factory is attached goes nowhere a reader will look. Runs first of the
/// startup services, so the catalogue's warm is logged. The two jobs are
/// <see cref="StoreLogAttachment"/> and <see cref="StoreStartupLines"/>, which is why this service
/// is handed two things and not seven.
/// </summary>
/// <param name="attachment">The host's loggers, attached to the stores.</param>
/// <param name="lines">The sentences about the stores and the signing key.</param>
public sealed class StoreAnnouncement(StoreLogAttachment attachment, StoreStartupLines lines) : IHostedService
{
    /// <summary>Attaches the loggers and writes the startup lines.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        attachment.Attach();
        lines.Write();
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
