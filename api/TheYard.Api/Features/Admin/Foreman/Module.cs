using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Admin.Foreman;

namespace TheYard.Api.Features.Admin.Foreman;

/// <summary>
/// The Foreman at rung 0 (ADR: The Foreman, a place to start): one page that observes and explains, read from the
/// Admin tab's own cards and the failures Application Insights kept, with no model and no action.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the page's reader and handler.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IForemanReadings>(provider => new ForemanReadings(
            provider.GetRequiredService<TheYard.Application.Features.Admin.Cloud.ICloudCards>(),
            provider.GetRequiredService<TheYard.Application.Features.Admin.Cloud.IIncidentsReader>(),
            provider.GetRequiredService<BuildInfo>(),
            provider.GetRequiredService<HostStart>(),
            TimeProvider.System));
        services.AddScoped<ReadForemanHandler>();
    }

    /// <summary>Maps GET /api/admin/foreman and GET /api/admin/foreman.md.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        // #region foreman-endpoint
        // Public like the cards it reads: the page holds nothing the Admin tab does not already show.
        app.MapGet("/api/admin/foreman", Page)
            .WithName("GetForeman")
            .WithTags("Admin")
            .WithSummary("The Foreman's page at rung 0: what happened, the readings, the likely cause, where to start and what it could not read");
        app.MapGet("/api/admin/foreman.md", Markdown)
            .WithName("GetForemanMarkdown")
            .WithTags("Admin")
            .WithSummary("The Foreman's page as the markdown the nightly run keeps");
        // #endregion foreman-endpoint
    }

    private static async Task<Ok<ForemanPage>> Page(ReadForemanHandler handler, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(new ReadForemanQuery(), cancellation));

    private static async Task<ContentHttpResult> Markdown(ReadForemanHandler handler, CancellationToken cancellation) =>
        TypedResults.Text((await handler.HandleAsync(new ReadForemanQuery(), cancellation)).Markdown, "text/markdown");
}
