// The blog's pictures from the site's own address (ADR-113, the addendum on where the pictures live; the
// standing rule that every asset is served from the site's own domain): GET /blog/images/{name}, a year's cache
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

        app.MapPost("/api/admin/blog/posts/{slug}/images", Upload)
            .WithName("PutBlogImage").WithTags("Admin")
            .WithSummary("Puts a picture on a post at its position: a multipart form with file, alt and position; the operator's key only")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .DisableAntiforgery();
        return app;
    }

    /// <summary>
    /// The operator's upload: the key first, then the form's file (read to one byte past the limit, so a file
    /// over it is refused by its size without being read whole), its alt text and its position.
    /// </summary>
    private static async Task<IResult> Upload(string slug, HttpContext http, AdminKey adminKey, PutBlogImageHandler handler, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.Problem(detail: $"Send the operator's key in {AdminKey.HeaderName}.", statusCode: 401, title: "The key is missing or wrong");
        }
        if (!http.Request.HasFormContentType || (await http.Request.ReadFormAsync(cancellation)) is not { } form || form.Files["file"] is not { } file)
        {
            return Results.Problem(detail: "The body is a multipart form with a file named file, an alt text named alt and a position named position.", statusCode: 400, title: "The picture could not be read");
        }
        using var stream = file.OpenReadStream();
        var buffer = new byte[Math.Min(file.Length, BlogImageHeaders.MaxBytes + 1)];
        int read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellation);
        byte[] bytes = buffer[..read];
        int position = int.TryParse(form["position"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int p) ? p : 0;
        var result = await handler.HandleAsync(new PutBlogImageCommand(slug, bytes, form["alt"].ToString(), position), cancellation);
        return result.Kept is { } kept
            ? Results.Json(new { position = kept.Position, url = kept.Url, alt_text = kept.AltText, width = kept.Width, height = kept.Height })
            : Results.Problem(detail: result.Wrong, statusCode: result.Status, title: result.Status switch
            {
                404 => "No such post",
                503 => "The blog cannot keep pictures now",
                _ => "The picture was refused",
            });
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
