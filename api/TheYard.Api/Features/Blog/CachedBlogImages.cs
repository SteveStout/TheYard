// The host's memory in front of the blog's picture container (ADR-113, the addendum on where the pictures
// live), so a post's slides cost one blob read a roll, not one a view.
using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

// #region cached-blog-images
/// <summary>
/// The kept pictures with the host's memory in front: least recently used, 32 MB and a day each, a picture
/// kept only once it was found, so one uploaded after a miss is served on the next ask.
/// </summary>
/// <param name="inner">The container.</param>
public sealed class CachedBlogImages(IBlogImageReader inner) : IBlogImageReader
{
    /// <summary>The most bytes the memory keeps.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, BlogImageTally Tally)? _tally;

    /// <summary>The count and bytes, listed at most once a minute.</summary>
    public async Task<BlogImageTally> TallyAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_tally is { } held && now - held.At < TimeSpan.FromMinutes(1))
            {
                return held.Tally;
            }
            var tally = await inner.TallyAsync(cancellation);
            _tally = (now, tally);
            return tally;
        }
        finally
        {
            _gate.Release();
        }
    }

    private readonly BoundedCache<string, BlogImageFile> _held = new(512, MaxBytes, TimeSpan.FromDays(1), file => file.Bytes.LongLength, comparer: StringComparer.Ordinal);

    public async Task<BlogImageFile?> OpenAsync(string name, CancellationToken cancellation)
    {
        if (_held.TryGet(name, out var held))
        {
            return held;
        }
        var file = await inner.OpenAsync(name, cancellation);
        if (file is not null)
        {
            _held.Set(name, file);
        }
        return file;
    }
}
// #endregion cached-blog-images
