// One resource card under Cloud infrastructure as the Admin tab reads it: the card by its key, or
// none, through the ICloudCards port, so the endpoint asks one handler and the handler is tested
// with fake cards and no host (ADR: The onion cut into vertical slices, rule 7).
namespace TheYard.Application.Features.Admin.Cloud;

// #region read-cloud-card-handler
/// <summary>Port: the resource cards, each by its key, built from what is kept.</summary>
public interface ICloudCards
{
    /// <summary>Whether a card by this key exists.</summary>
    /// <param name="key">sqldb, cosmosdb, registry, insights, mail or edge.</param>
    bool Knows(string key);

    /// <summary>The card's report for the window.</summary>
    /// <param name="key">A key the cards know.</param>
    /// <param name="window">24h, 7d or 30d.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<CloudReport> ReadAsync(string key, string? window, CancellationToken cancellation);
}

/// <summary>One card for one window; an unknown key is no card, and an unknown window is the day.</summary>
/// <param name="cards">The resource cards.</param>
public sealed class ReadCloudCardHandler(ICloudCards cards)
{
    /// <summary>The card's report, or null when no card has the key.</summary>
    /// <param name="key">The card's key from the route.</param>
    /// <param name="window">The window the page asked for, or none.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<CloudReport?> HandleAsync(string key, string? window, CancellationToken cancellation) =>
        cards.Knows(key) ? await cards.ReadAsync(key, CardWindows.Of(window), cancellation) : null;
}
// #endregion read-cloud-card-handler
