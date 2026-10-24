// Putting a picture into a post (ADR-113, the addendum on putting a picture in): the operator's one write path
// for a picture. The handler holds the picture to its limits by what its own bytes say (one megabyte, PNG,
// JPEG or WebP by the magic numbers, 2000 pixels wide, an alt text that says what it shows), keeps it under
// the hash of its bytes, and puts its row on the post at its position. No resizing here: the post lane
// resizes before it uploads.
using System.Globalization;
using TheYard.Application.Shared;
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

// #region put-blog-image
/// <summary>A picture to put on a post.</summary>
/// <param name="Slug">The post.</param>
/// <param name="Bytes">The picture.</param>
/// <param name="AltText">One sentence saying what the picture shows.</param>
/// <param name="Position">Its place among the post's pictures, from 1; a picture already there is replaced.</param>
public sealed record PutBlogImageCommand(string Slug, byte[] Bytes, string AltText, int Position);

/// <summary>What putting a picture came to: the row kept, or the status and the sentence why not.</summary>
/// <param name="Kept">The post's new picture row; null when it was refused.</param>
/// <param name="Status">400 for a picture the limits refuse, 404 for no such post, 503 for a store that cannot keep now; null when kept.</param>
/// <param name="Wrong">The sentence naming the limit or the reason; null when kept.</param>
public sealed record PutBlogImageResult(BlogImage? Kept, int? Status, string? Wrong);

/// <summary>Holds a picture to its limits, keeps it, and puts its row on the post.</summary>
/// <param name="store">Where the posts are kept.</param>
/// <param name="images">Where the pictures are kept.</param>
/// <param name="clock">The clock the post's row is stamped with.</param>
public sealed class PutBlogImageHandler(IBlogStore store, IBlogImageWriter images, TimeProvider clock) : ICommandHandler<PutBlogImageCommand, PutBlogImageResult>
{
    /// <summary>The row kept, or why not.</summary>
    /// <param name="command">The picture and its post.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<PutBlogImageResult> HandleAsync(PutBlogImageCommand command, CancellationToken cancellation)
    {
        var header = BlogImageHeaders.Read(command.Bytes);
        string? wrong = command.Bytes.Length > BlogImageHeaders.MaxBytes ? $"a picture is at most {BlogImageHeaders.MaxBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes (one megabyte), and this is over it; resize it first"
            : header is null ? "a picture is a PNG, a JPEG or a WebP by its own bytes, and these are none of the three"
            : header.Width > BlogImageHeaders.MaxWidth ? $"a picture is at most {BlogImageHeaders.MaxWidth} pixels wide, and this is {header.Width}; resize it first"
            : header.Width <= 0 || header.Height <= 0 ? "the picture's header states no size"
            : command.AltText.Trim().Length is 0 or > 300 ? "the alt text is one sentence saying what the picture shows, 1 to 300 characters"
            : command.Position is < 1 or > 50 ? "the position is the picture's place in the post, 1 to 50"
            : null;
        if (wrong is not null)
        {
            return new(null, 400, wrong);
        }
        var availability = await store.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return new(null, 503, availability.Reason);
        }
        if (await store.PostAsync(command.Slug, cancellation) is not { } post)
        {
            return new(null, 404, $"no published post has the slug {command.Slug}");
        }
        string name = BlogImageNames.For(command.Bytes, header!.Kind);
        await images.PutAsync(name, new BlogImageFile(command.Bytes, BlogImageNames.Kinds[header.Kind]), cancellation);
        var row = new BlogImage(command.Position, $"/blog/images/{name}", command.AltText.Trim(), header.Width, header.Height);
        await store.SaveAsync(post with { Images = [.. post.Images.Where(i => i.Position != command.Position).Append(row).OrderBy(i => i.Position)] }, clock.GetUtcNow(), cancellation);
        return new(row, null, null);
    }
}
// #endregion put-blog-image
