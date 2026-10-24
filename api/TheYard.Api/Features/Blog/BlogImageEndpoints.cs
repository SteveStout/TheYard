// The blog's pictures from the site's own address (ADR-113, the addendum on where the pictures live; the
// 9/19 rule that every asset is served from the site's own domain): GET /blog/images/{name}, a year's cache
// because a name is its bytes' hash, and the host's own memory in front of the container so a post's slides
// cost one blob read a roll, not one a view.
using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

// #region blog-image-endpoints
/// <summary>The one route that serves a kept picture.</summary>
public static class BlogImageEndpoints
{
    /// <summary>What a picture is cached for: a year, and never revalidated, because its name changes with its bytes.</summary>
    public const string CacheRule = "public, max-age=31536000, immutable";

    /// <summary>Maps GET /blog/images/{name}.</summary>
    public static IEndpointRouteBuilder MapBlogImages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/blog/images/{name}", Picture)
            .WithName("GetBlogImage").WithTags("Blog")
            .WithSummary("One of the blog's pictures by its kept name, the hash of its bytes and its kind")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/webp", "image/png", "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<IResult> Picture(string name, ReadBlogImageHandler handler, HttpContext http, CancellationToken cancellation)
    {
        if (await handler.HandleAsync(new ReadBlogImageQuery(name), cancellation) is not { } file)
        {
            return Results.Problem(detail: "No picture is kept under that name.", statusCode: 404, title: "No such picture");
        }
        http.Response.Headers.CacheControl = CacheRule;
        http.Response.Headers.ETag = $"\"{name}\"";
        return Results.File(file.Bytes, file.ContentType);
    }
}

// #endregion blog-image-endpoints
