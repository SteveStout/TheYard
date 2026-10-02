// The Admin tab's cost and telemetry handlers: what Azure charges for the site, and the last
// hour as Application Insights has it. Its own file because both read from outside this
// process (the kept cost days and Application Insights) rather than from its own rings.

namespace TheYard.Api;

/// <summary>The cost and telemetry handlers of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers what Azure charges for the site over the named window, read from the kept days.</summary>
    private static async Task<IResult> Costs(string? window, CostHistoryReader costs, CancellationToken cancellation) =>
        Results.Json(await costs.ReadAsync(window, cancellation));

    /// <summary>
    /// Answers the last hour as Application Insights has it, in a shape the card can render even when telemetry is
    /// off.
    /// </summary>
    private static async Task<IResult> Telemetry(TelemetryReader telemetry) =>
        Results.Json(await telemetry.GetRecentAsync());
}
