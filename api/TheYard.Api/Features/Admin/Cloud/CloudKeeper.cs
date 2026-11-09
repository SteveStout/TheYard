// Keeps every resource card's window that somebody asked for in the last hour built, once a minute
// (ADR: One frame for every resource, the addendum on building behind the request), so the card route answers from a
// view already built and Azure is asked here, never on a request's path.

namespace TheYard.Api.Features.Admin.Cloud;

// #region cloud-keeper
/// <summary>
/// Once a minute, builds again each window the reader was asked for in the last hour; a window nobody
/// asks for drops out after the hour, so nothing is read for a page nobody opens.
/// </summary>
public sealed class CloudKeeper(CloudReader reader, TimeProvider clock, ILogger<CloudKeeper> logger) : BackgroundService
{
    /// <summary>The minute loop: every watched window built, a failure logged by type, until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await reader.KeepWatchedAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("The Cloud cards could not be kept built ({Exception})", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion cloud-keeper
