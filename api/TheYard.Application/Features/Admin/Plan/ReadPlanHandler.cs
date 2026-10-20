// The plan card as the Admin tab reads it: one window of the App Service plan, answered from the
// keep through the IPlanCard port, so the endpoint asks one handler and the handler is tested with
// a fake card and no host (ADR: The onion cut into vertical slices, rule 7).
namespace TheYard.Application.Features.Admin.Plan;

// #region read-plan-handler
/// <summary>Port: one window of the plan card, built from what is kept.</summary>
public interface IPlanCard
{
    /// <summary>The window's report; an unknown window is read as the day.</summary>
    /// <param name="window">24h, 7d or 30d, or anything else for the day.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<CloudReport> ReadAsync(string? window, CancellationToken cancellation);
}

/// <summary>
/// One window of the plan. The windows the card offers are the tab's shared three; any other word
/// is read as the day, so a stale link never answers an error.
/// </summary>
/// <param name="card">The plan card's reads.</param>
public sealed class ReadPlanHandler(IPlanCard card)
{
    /// <summary>The window's report.</summary>
    /// <param name="window">The window the page asked for, or none.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<CloudReport> HandleAsync(string? window, CancellationToken cancellation) =>
        card.ReadAsync(MachineWindows.Parse(window) is { } chosen ? chosen.Name : "24h", cancellation);
}
// #endregion read-plan-handler
