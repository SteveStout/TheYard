using System.Text.Json;

namespace TheYard.Api;

/// <summary>
/// The one reading behind the first screen: how fast the site answers, what it costs this month,
/// and how long it has been up (ADR: One reading for the first screen).
/// </summary>
public static class PulseEndpoints
{
    /// <summary>The wire's own spelling, as every other answer here.</summary>
    private static readonly JsonSerializerOptions WireFormat = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Maps /api/pulse.</summary>
    public static IEndpointRouteBuilder MapPulseEndpoints(this IEndpointRouteBuilder app)
    {
        // #region pulse-route
        // Public, like the readings it carries: the cost cards and the machines card are already
        // public on the Admin tab by the owner's own ruling (ADR: What Azure charges). One read
        // rather than those two, because the landing page is what a stranger opens and the two
        // Admin reads behind these figures are the heaviest on the site. The answer is kept for
        // fifteen seconds, so a page open on a desk costs four reads a minute however many people
        // are looking at it.
        app.MapGet("/api/pulse", Read)
            .WithName("GetPulse")
            .WithTags("Health")
            .WithSummary("How fast the site is answering, what it costs this month, and how long it has been up")
            .WithDescription("The first screen's one reading, built from the same sources as the Admin tab's machines and cost cards and kept for fifteen seconds. Every figure is nullable; a null means unread, never zero.")
            .Produces<PulseReply>(StatusCodes.Status200OK);
        // #endregion pulse-route
        return app;
    }

    /// <summary>Answers the pulse, in the snake_case the wire is written in.</summary>
    private static async Task<IResult> Read(PulseReader pulse, CancellationToken cancellation) =>
        Results.Json(await pulse.ReadAsync(cancellation), WireFormat);
}
