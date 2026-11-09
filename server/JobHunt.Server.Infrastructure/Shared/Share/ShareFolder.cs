using System.Text;

namespace JobHunt.Server.Infrastructure.Shared.Share;

/// <summary>
/// The share through its rclone mount. A read retries, because the mount can answer an I/O error while it catches up
/// with a file the share has grown, and a second look usually clears it. Every write is held to the write policy first.
/// </summary>
public sealed class ShareFolder : IShare
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan[] Waits = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15)];

    /// <summary>A share at a root, with any link in the root's path resolved.</summary>
    /// <param name="root">The mount, or a link to it.</param>
    public ShareFolder(string root)
    {
        Root = new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(root);
    }

    /// <inheritdoc/>
    public string Root { get; }

    /// <inheritdoc/>
    public string Full(string relative) => relative.Length == 0 ? Root : Path.Combine(Root, relative.Replace('\\', '/'));

    /// <inheritdoc/>
    public async Task<string> ReadAsync(string relative, CancellationToken ct)
    {
        byte[] bytes = await RetryAsync(() => File.ReadAllBytesAsync(Full(relative), ct), ct);
        return Decode(bytes);
    }

    /// <inheritdoc/>
    public Task<string> TailAsync(string relative, int bytes, CancellationToken ct) =>
        RetryAsync(
            async () =>
            {
                await using var stream = new FileStream(Full(relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(Math.Max(0, stream.Length - bytes), SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Utf8);
                return await reader.ReadToEndAsync(ct);
            },
            ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ShareFile>> ListAsync(string folder, string pattern, CancellationToken ct)
    {
        string full = Full(folder);
        IReadOnlyList<ShareFile> files = Directory.Exists(full)
            ? [.. Directory.EnumerateFiles(full, pattern).Select(path => new ShareFile(Path.GetRelativePath(Root, path).Replace('\\', '/'), File.GetLastWriteTimeUtc(path)))]
            : [];
        return Task.FromResult(files);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> FoldersAsync(string folder, string pattern, CancellationToken ct)
    {
        string full = Full(folder);
        IReadOnlyList<string> folders = Directory.Exists(full)
            ? [.. Directory.EnumerateDirectories(full, pattern).Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))]
            : [];
        return Task.FromResult(folders);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string relative, CancellationToken ct) => Task.FromResult(File.Exists(Full(relative)));

    /// <inheritdoc/>
    public async Task WriteAsync(string relative, string text, CancellationToken ct)
    {
        string full = Allowed(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text, Utf8, ct);
    }

    /// <inheritdoc/>
    public async Task WriteNewAsync(string relative, string text, CancellationToken ct)
    {
        string full = Allowed(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
        {
            await stream.WriteAsync(Utf8.GetBytes(text), ct);
        }
        if (await ReadAsync(relative, ct) != text)
        {
            throw new IOException($"{relative} did not read back as written");
        }
    }

    /// <inheritdoc/>
    public async Task AppendAsync(string relative, string text, CancellationToken ct)
    {
        string full = Allowed(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.AppendAllTextAsync(full, text, Utf8, ct);
    }

    /// <inheritdoc/>
    public Task CopyInAsync(string from, string relative, CancellationToken ct)
    {
        string full = Allowed(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(from, full, overwrite: true);
        return Task.CompletedTask;
    }

    private static string Decode(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? Utf8.GetString(bytes, 3, bytes.Length - 3) : Utf8.GetString(bytes);

    private string Allowed(string relative) =>
        WritePolicy.Allowed(relative) ? Full(relative) : throw new UnauthorizedAccessException($"{relative} is outside what this process may write");

    private static async Task<T> RetryAsync<T>(Func<Task<T>> read, CancellationToken ct)
    {
        foreach (TimeSpan wait in Waits)
        {
            try
            {
                return await read();
            }
            catch (IOException failure) when (failure is not FileNotFoundException and not DirectoryNotFoundException)
            {
                await Task.Delay(wait, ct);
            }
        }
        return await read();
    }
}
