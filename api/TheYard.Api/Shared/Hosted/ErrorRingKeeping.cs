// The two error rings offered to the kept log, and the moment the process started, both set when
// the host starts.

namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Wires the server's and the browser's error rings to the kept log, so an error outlives the
/// container (ADR: Logs that outlive the container), and marks the start the health report
/// measures uptime from.
/// </summary>
public sealed class ErrorRingKeeping(ErrorRings errorRings, KeptRingWriter keptRings, HostStart hostStart) : IHostedService
{
    /// <summary>Marks the start and wires both rings.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        hostStart.Mark();
        // #region two-rings
        // Browser reports get their own fifty slots rather than sharing the server's.
        // POST /api/errors/client is anonymous, so in a shared ring fifty posts from
        // anybody would push every server error off the page an operator diagnoses
        // an outage from; in their own ring, a flood of reports can only push out
        // other reports. The Admin tab still shows the two merged, because one place
        // to look is the point (ADR: Error handling, one shape everywhere).
        errorRings.Server.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);
        errorRings.Browser.Kept = entry => keptRings.Keep(KeptRings.Errors, entry.At, entry);
        // #endregion two-rings
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
