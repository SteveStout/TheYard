// The Admin tab's cost and telemetry routes: what Azure charges for the site, and the last hour as Application
// Insights has it. Each asks its one query handler (ADR-115's fourth rung).
using TheYard.Application.Features.Admin.Costs;
using TheYard.Application.Features.Admin.Operations;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The cost and telemetry routes of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers the cost card over 24h, 7d or 30d.</summary>
    private static async Task<IResult> Costs(string? window, ReadCostsHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadCostsQuery(window), cancellation));

    /// <summary>Answers the last hour as Application Insights has it.</summary>
    private static async Task<IResult> Telemetry(ReadTelemetryHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadTelemetryQuery(), cancellation));
}
