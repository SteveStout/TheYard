// The blog's pictures (ADR-113, the addendum on where the pictures live): a picture is bytes kept under its
// hashed name in a blob container, read through one port and written through another, so the query that
// serves a picture can hold the reader only (ADR-115). The container is the one adapter on Azure; a process
// with no container keeps them in memory and says so, the way the blog's posts do.
using System.Collections.Concurrent;
using TheYard.Application.Shared;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

// #region blog-images
/// <summary>One kept picture: its bytes and the type it is served with.</summary>
/// <param name="Bytes">The picture.</param>
/// <param name="ContentType">image/webp, image/png or image/jpeg.</param>
public sealed record BlogImageFile(byte[] Bytes, string ContentType);

/// <summary>Port: reads a kept picture by its name.</summary>
public interface IBlogImageReader
{
    /// <summary>The picture kept under the name, or null when none is.</summary>
    /// <param name="name">A name BlogImageNames made.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<BlogImageFile?> OpenAsync(string name, CancellationToken cancellation);

    /// <summary>How many pictures are kept and their bytes together, with where that was read.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<BlogImageTally> TallyAsync(CancellationToken cancellation);
}

/// <summary>The kept pictures counted.</summary>
/// <param name="Count">How many; null when the listing could not be read.</param>
/// <param name="Bytes">Their bytes together.</param>
/// <param name="Source">Where it was read, or why it was not.</param>
public sealed record BlogImageTally(int? Count, long Bytes, string Source);

/// <summary>Port: keeps a picture under its name.</summary>
public interface IBlogImageWriter
{
    /// <summary>Keeps the picture; a name already kept is the same bytes, so writing it again changes nothing.</summary>
    /// <param name="name">A name BlogImageNames made from these bytes.</param>
    /// <param name="file">The picture and its type.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task PutAsync(string name, BlogImageFile file, CancellationToken cancellation);
}

/// <summary>The pictures kept in this process's memory, for the tests and for a process with no container; ends with the process.</summary>
public sealed class MemoryBlogImageStore : IBlogImageReader, IBlogImageWriter
{
    /// <summary>The pictures by name: the ones a test or the operator put, a handful.</summary>
    private readonly ConcurrentDictionary<string, BlogImageFile> _files = new(StringComparer.Ordinal);

    /// <summary>How many times a picture was read, so a test can see a cache answer in its place.</summary>
    public int Reads { get; private set; }

    public Task<BlogImageFile?> OpenAsync(string name, CancellationToken cancellation)
    {
        Reads++;
        return Task.FromResult(_files.TryGetValue(name, out var file) ? file : null);
    }

    public Task PutAsync(string name, BlogImageFile file, CancellationToken cancellation)
    {
        _files[name] = file;
        return Task.CompletedTask;
    }

    public Task<BlogImageTally> TallyAsync(CancellationToken cancellation) =>
        Task.FromResult(new BlogImageTally(_files.Count, _files.Values.Sum(f => f.Bytes.LongLength), "this process's memory: the pictures end with it; no container is configured here"));
}

/// <summary>Asks for one picture by its kept name.</summary>
/// <param name="Name">The name from the route.</param>
public sealed record ReadBlogImageQuery(string Name);

/// <summary>One picture by its name; null for a name the rule never makes or a picture not kept.</summary>
/// <param name="images">The kept pictures.</param>
public sealed class ReadBlogImageHandler(IBlogImageReader images) : IQueryHandler<ReadBlogImageQuery, BlogImageFile?>
{
    /// <summary>The picture, or null.</summary>
    /// <param name="query">The name.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<BlogImageFile?> HandleAsync(ReadBlogImageQuery query, CancellationToken cancellation) =>
        BlogImageNames.IsName(query.Name) ? await images.OpenAsync(query.Name, cancellation) : null;
}
// #endregion blog-images
