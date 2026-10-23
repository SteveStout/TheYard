// The sweep over every address this container serves, run once the server listens and once more
// when the process has settled.
namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Every roll carries a check of the thing that was just rolled (ADR: Every page, checked at every
/// roll). The sweep dials the address the server bound, read when it runs (SelfAddress.cs); under
/// the test host there is none, so the suite drives the runner with its own client instead.
/// </summary>
public sealed class PageSweepAtRoll(PageStatusRunner pageStatus, IHostApplicationLifetime lifetime) : IHostedService
{
    /// <summary>Arranges both sweeps for when the server is listening.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // #region page-status-wiring
        lifetime.ApplicationStarted.Register(() => pageStatus.TryStart("roll"));
        // And once more when the process has settled: the roll's sweep runs while the
        // catalogues are warming, and a reading taken then is a reading of the start, not of
        // the site. The second is what the tile shows until somebody asks for another.
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            await Task.Delay(PageStatusRunner.SecondSweep);
            pageStatus.TryStart("settled");
        }));
        // #endregion page-status-wiring
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
