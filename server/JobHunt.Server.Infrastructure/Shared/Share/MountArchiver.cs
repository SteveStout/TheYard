using System.IO.Compression;
using System.Security.Cryptography;

namespace JobHunt.Server.Infrastructure.Shared.Share;

/// <summary>
/// The archive job's zip, verify and move over the share's rclone mount. A unit is walked inside itself only, never
/// above it; a zip is written whole at its first free name and reopened from disk; the originals move by one rename,
/// which the mount sends to the share as one server-side move. Every target is held to the write policy first, every
/// source must sit in one of the job's five folders, and nothing here deletes. Each listing, open and rename counts as
/// one call to the share.
/// </summary>
/// <param name="share">The share, for its root and its write policy.</param>
public sealed class MountArchiver(IShare share) : IArchiver
{
    private static readonly IReadOnlyList<string> Sources = ["nightly/", "dispatch/done/", "dispatch/locks", "inbox/link-tokens/merged", "mentor/logs"];

    /// <inheritdoc/>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ArchiveEntry>> ReadAsync(ArchiveUnit unit, CancellationToken ct)
    {
        var entries = new List<ArchiveEntry>();
        foreach (string path in Paths(unit))
        {
            await using var stream = OpenRead(path);
            var (length, crc) = await Crc32.OfAsync(stream, ct);
            entries.Add(new ArchiveEntry(path, length, crc));
        }
        return entries;
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(ArchiveUnit unit, CancellationToken ct) => Task.FromResult(Paths(unit).Count);

    /// <inheritdoc/>
    public async Task<ArchiveZip> ZipAsync(ArchiveUnit unit, string zip, CancellationToken ct)
    {
        string target = Free(zip, ".zip");
        string full = Allowed(target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Calls++;
        await using (var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
        await using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            foreach (string path in Paths(unit))
            {
                ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                Calls++;
                DateTime written = File.GetLastWriteTimeUtc(share.Full(path));
                entry.LastWriteTime = written.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : new DateTimeOffset(written, TimeSpan.Zero);
                await using var from = OpenRead(path);
                await using var into = await entry.OpenAsync(ct);
                await from.CopyToAsync(into, ct);
            }
        }
        Calls++;
        await using var back = new FileStream(full, FileMode.Open, FileAccess.Read);
        byte[] hash = await SHA256.HashDataAsync(back, ct);
        return new ArchiveZip(target, back.Length, Convert.ToHexStringLower(hash));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ArchiveEntry>> OpenAsync(string zip, CancellationToken ct)
    {
        Calls++;
        await using var input = new FileStream(share.Full(zip), FileMode.Open, FileAccess.Read);
        await using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entries = new List<ArchiveEntry>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            await using var stream = await entry.OpenAsync(ct);
            var (length, crc) = await Crc32.OfAsync(stream, ct);
            // An entry whose bytes do not come back to the CRC its own header carries is broken whatever its bytes say,
            // so it answers a length no source file has and the compare fails on it.
            entries.Add(new ArchiveEntry(entry.FullName, crc == entry.Crc32 ? length : -1, crc));
        }
        return entries;
    }

    /// <inheritdoc/>
    public Task<string> MoveAsync(ArchiveUnit unit, string target, CancellationToken ct)
    {
        Source(unit.Source);
        Directory.CreateDirectory(Path.GetDirectoryName(Allowed($"{target}/{unit.Name}"))!);
        if (!unit.IsFiles)
        {
            string to = Free($"{target}/{unit.Name}", string.Empty);
            Allowed(to);
            Calls++;
            Directory.Move(share.Full(unit.Source), share.Full(to));
            return Task.FromResult(to);
        }
        foreach (string name in unit.Files)
        {
            string to = Free($"{target}/{name}", string.Empty);
            Allowed(to);
            Calls++;
            File.Move(share.Full($"{unit.Source}/{name}"), share.Full(to));
        }
        return Task.FromResult($"{target}/");
    }

    /// <inheritdoc/>
    public Task<string> FailAsync(string zip, CancellationToken ct)
    {
        string aside = Free(zip + ".failed", string.Empty);
        Allowed(aside);
        Calls++;
        File.Move(share.Full(zip), share.Full(aside));
        return Task.FromResult(aside);
    }

    /// <summary>The unit's files as paths relative to the share's root: a folder walked inside itself, or the named files.</summary>
    private List<string> Paths(ArchiveUnit unit)
    {
        Source(unit.Source);
        string root = share.Full(unit.Source);
        if (unit.IsFiles)
        {
            Calls++;
            return [.. unit.Files.Where(name => File.Exists(Path.Combine(root, name))).Select(name => $"{unit.Source}/{name}")];
        }
        if (!Directory.Exists(root))
        {
            return [];
        }
        var paths = new List<string>();
        var folders = new Stack<string>([root]);
        while (folders.TryPop(out string? folder))
        {
            Calls++;
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                folders.Push(child);
            }
            paths.AddRange(Directory.EnumerateFiles(folder).Select(file => Path.GetRelativePath(share.Root, file).Replace('\\', '/')));
        }
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private FileStream OpenRead(string path)
    {
        Calls++;
        return new FileStream(share.Full(path), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>The path, or the same with -2, -3 and on before its extension, whichever is not taken yet.</summary>
    private string Free(string path, string extension)
    {
        string stem = extension.Length > 0 && path.EndsWith(extension, StringComparison.Ordinal) ? path[..^extension.Length] : path;
        string candidate = stem + extension;
        for (int n = 2; Exists(candidate); n++)
        {
            candidate = $"{stem}-{n}{extension}";
        }
        return candidate;
    }

    private bool Exists(string path)
    {
        Calls++;
        string full = share.Full(path);
        return File.Exists(full) || Directory.Exists(full);
    }

    private string Allowed(string relative) =>
        WritePolicy.Allowed(relative) ? share.Full(relative) : throw new UnauthorizedAccessException($"{relative} is outside what this process may write");

    private static void Source(string relative)
    {
        string path = relative.Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal) || !Sources.Any(source => path.StartsWith(source, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException($"{relative} is not in a folder the archive job takes from");
        }
    }
}
