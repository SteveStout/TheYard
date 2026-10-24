// The Admin tab's cost and telemetry routes: what Azure charges for the site, and the last hour as Application
// Insights has it. Each asks its one query handler (ADR-115's fourth rung).
using TheYard.Application.Features.Admin.Costs;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The cost and telemetry routes of AdminEndpoints; mapped in AdminEndpoints.cs.</summary>
public static class AdminCostRoutes
{
    /// <summary>Answers the cost card over 24h, 7d or 30d.</summary>
    internal static async Task<IResult> Costs(string? window, ReadCostsHandler handler, CancellationToken cancellation) =>
        AdminEndpoints.Answer(await handler.HandleAsync(new ReadCostsQuery(window), cancellation));

    /// <summary>Answers the last hour as Application Insights has it.</summary>
    internal static async Task<IResult> Telemetry(ReadTelemetryHandler handler, CancellationToken cancellation) =>
        AdminEndpoints.Answer(await handler.HandleAsync(new ReadTelemetryQuery(), cancellation));
}
