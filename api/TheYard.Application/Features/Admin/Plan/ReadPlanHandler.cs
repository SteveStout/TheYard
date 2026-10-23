// The plan card as the Admin tab reads it: one window of the App Service plan, answered from the
// keep through the IPlanCard port, so the endpoint asks one handler and the handler is tested with
// a fake card and no host (ADR: The onion cut into vertical slices, rule 7).
using TheYard.Application.Shared;

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
public sealed class ReadPlanHandler(IPlanCard card) : IQueryHandler<ReadPlanQuery, CloudReport>
{
    /// <summary>The window's report.</summary>
    /// <param name="query">The window the page asked for, or none.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<CloudReport> HandleAsync(ReadPlanQuery query, CancellationToken cancellation) =>
        card.ReadAsync(CardWindows.Of(query.Window), cancellation);
}

/// <summary>One window of the plan.</summary>
/// <param name="Window">24h, 7d or 30d, or none.</param>
public sealed record ReadPlanQuery(string? Window);
// #endregion read-plan-handler
