// What Azure charges for the site, as a query (ADR-115's fourth rung).
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Admin.Costs;

// #region costs-handler
/// <summary>The kept cost days, as the cost card draws them.</summary>
public interface ICostReadings
{
    /// <summary>The spend, the forecast and each resource over 24h, 7d or 30d.</summary>
    Task<object> ReadAsync(string? window, CancellationToken cancellation);
}

/// <summary>The cost card over a window.</summary>
/// <param name="costs">The kept cost days.</param>
public sealed class ReadCostsHandler(ICostReadings costs) : IQueryHandler<ReadCostsQuery, Reading>
{
    /// <summary>The card.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadCostsQuery query, CancellationToken cancellation) => Reading.Of(await costs.ReadAsync(query.Window, cancellation));
}

/// <summary>Asks for the cost card.</summary>
/// <param name="Window">24h, 7d or 30d.</param>
public sealed record ReadCostsQuery(string? Window);
// #endregion costs-handler
