// Keeps every Admin route's views that somebody asked for in the last hour built, once a minute (ADR: One frame
// for every resource, the addenda on building behind the request), so the Cloud cards, the plan card, the Azure
// card and the runs route answer from a view already built and Azure is asked here, never on a request's path.

namespace TheYard.Api.Shared.Hosted;

// #region view-keeper
/// <summary>
/// Once a minute, on a loop of its own for each route, builds again each slot the route was asked for in the last hour; a slot nobody asks for
/// drops out after the hour, so nothing is read for a page nobody opens.
/// </summary>
/// <param name="views">Each route's kept views.</param>
/// <param name="clock">The clock the minute is waited on.</param>
/// <param name="logger">Where a pass that fails is logged, by the views' name and the exception's type.</param>
public sealed class ViewKeeper(IEnumerable<IKeptViews> views, TimeProvider clock, ILogger<ViewKeeper> logger) : BackgroundService
{
    private readonly IReadOnlyList<IKeptViews> _views = [.. views];

    /// <summary>One minute loop a route, each on its own, so a slow route never holds up another's refresh.</summary>
    /// <param name="stoppingToken">The host's stopping.</param>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_views.Select(kept => Task.Run(() => KeepAsync(kept, stoppingToken), CancellationToken.None)));

    /// <summary>One route's minute loop: every watched slot built, a failure logged by type, until the host stops.</summary>
    private async Task KeepAsync(IKeptViews kept, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await kept.KeepWatchedAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("The {Views} could not be kept built ({Exception})", kept.Name, ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion view-keeper
