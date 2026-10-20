using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;
using TheYard.Application.Features.Admin.Plan;

namespace TheYard.Api.Features.Admin.Plan;

/// <summary>The App Service plan card's one route: a window of the plan, from the keep.</summary>
public static class PlanEndpoints
{
    /// <summary>Maps GET /api/admin/plan.</summary>
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        // #region plan-endpoint
        // Public like every reading on the Admin tab: the plan's CPU and memory are no more
        // private than the machines card's. Answered from the keep; Azure is asked only when the
        // newest kept minute is stale, and only on the SQL site, where the recorder runs.
        app.MapGet("/api/admin/plan", Plan)
            .WithName("GetPlan")
            .WithTags("Admin")
            .WithSummary("The App Service plan: CPU, memory, its apps and what Azure did to it, for 24h, 7d or 30d");
        // #endregion plan-endpoint
        return app;
    }

    private static async Task<Ok<CloudReport>> Plan(string? window, ReadPlanHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(window, cancellation));
}
