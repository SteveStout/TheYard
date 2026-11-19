using System.Security.Cryptography;

namespace JobHunt.Server.Infrastructure.Shared.Cli;

/// <summary>
/// The tailored build, and when it stops, the send copy the newest resume lock names (ResumeLock). The copy is found by
/// the name the lock writes, never by a glob, and its bytes are hashed against the lock's line before it is offered, so
/// a row is never held a second night for a resume build and never goes out with bytes the lock does not vouch for.
/// </summary>
/// <param name="inner">The tailored build.</param>
/// <param name="share">The share, whose root holds the lock files and the send copy.</param>
public sealed class LockedSendCopy(IResumeBuilder inner, IShare share) : IResumeBuilder
{
    /// <inheritdoc/>
    public async Task<ResumeBuild> BuildAsync(string title, string company, string folder, CancellationToken ct)
    {
        ResumeBuild built = await inner.BuildAsync(title, company, folder, ct);
        if (built.Failure is not { } failure)
        {
            return built;
        }
        IReadOnlyList<ShareFile> locks = await share.ListAsync(string.Empty, "RESUME-LOCK-*.md", ct);
        string? newest = ResumeLock.Newest(locks.Select(file => Path.GetFileName(file.Path)));
        LockedCopy? copy = newest is null ? null : ResumeLock.Read(newest, await share.ReadAsync(newest, ct));
        string path = copy is null ? string.Empty : share.Full(copy.Name);
        return ResumeLock.Fallback(failure, copy, path, copy is null ? null : await ShaAsync(path, ct));
    }

    private static async Task<string?> ShaAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        await using FileStream bytes = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, ct));
    }
}
