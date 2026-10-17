namespace TheYard.Api;

/// <summary>
/// What colleagues say, served from data/recommendations.json so the two pages that show it quote
/// one file (ADR: The sidebar, the addendum on the column on the right).
/// </summary>
public static class RecommendationEndpoints
{
    /// <summary>Maps /api/recommendations.</summary>
    public static IEndpointRouteBuilder MapRecommendationEndpoints(this IEndpointRouteBuilder app)
    {
        // #region recommendations-route
        // The landing page's column and About Steven's both read this, so the words live in one
        // file and the two pages cannot quote different things. Public like the pages that show
        // it: every word is already public on LinkedIn, by its writer's choice. An entry out of
        // shape is answered as a failure that names the slip rather than as a column with a hole
        // in it; the gate reads the same check, so a shipped build never gets that far.
        app.MapGet("/api/recommendations", Read)
            .WithName("GetRecommendations")
            .WithTags("Documents")
            .WithSummary("The recommendations colleagues wrote on LinkedIn, in the order the site shows them")
            .WithDescription("Every entry has the same six fields: the writer's name and title, how they worked with Steve, the month, the recommendation as paragraphs, and the one sentence a short place quotes. They come from one file, data/recommendations.json, which is the one place to add or adjust one.")
            .Produces<RecommendationsReply>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        // #endregion recommendations-route
        return app;
    }

    /// <summary>Answers the file in shape, a 404 when the build shipped none, and a 500 that names the slip when an entry is out of shape.</summary>
    private static IResult Read(HostPaths paths)
    {
        if (!File.Exists(paths.RecommendationsPath))
        {
            return Results.Problem(
                detail: "No recommendations shipped with this build. They live in data/recommendations.json.",
                statusCode: StatusCodes.Status404NotFound,
                title: "No recommendations");
        }

        var reply = RecommendationsFile.Of(paths.RecommendationsPath);
        var problems = RecommendationsFile.Problems(reply);
        if (problems.Count > 0)
        {
            return Results.Problem(
                detail: "data/recommendations.json is out of shape: " + string.Join("; ", problems),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Recommendations out of shape");
        }

        return Results.Json(reply, RecommendationsFile.Format);
    }
}
