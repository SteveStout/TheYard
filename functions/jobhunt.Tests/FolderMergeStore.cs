
namespace JobHunt.Functions.Tests;

/// <summary>The merger's store over a plain folder on disk, so the rules can be tested without Azure.</summary>
public sealed class FolderMergeStore(string root) : IMergeStore
{
    private string Full(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(File.Exists(Full(path)));

    public Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        Reads++;
        return File.ReadAllBytesAsync(Full(path), ct);
    }

    public Task<MergeEntry?> StatAsync(string path, CancellationToken ct)
    {
        Stats++;
        var file = new FileInfo(Full(path));
        return Task.FromResult(file.Exists ? new MergeEntry(file.Name, file.LastWriteTimeUtc, file.Length) : null);
    }

    /// <summary>How many times a file was read whole.</summary>
    public int Reads { get; private set; }

    /// <summary>How many times only a file's properties were asked for.</summary>
    public int Stats { get; private set; }

    public Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct)
    {
        string full = Full(folder);
        IReadOnlyList<MergeEntry> entries = Directory.Exists(full)
            ? Directory.GetFiles(full).Select(f => new MergeEntry(Path.GetFileName(f), File.GetLastWriteTimeUtc(f), new FileInfo(f).Length)).ToList()
            : [];
        return Task.FromResult(entries);
    }

    public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct)
    {
        string full = Full(folder);
        IReadOnlyList<string> names = Directory.Exists(full) ? Directory.GetDirectories(full).Select(Path.GetFileName).Select(n => n!).ToList() : [];
        return Task.FromResult(names);
    }

    public Task EnsureFolderAsync(string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(Full(folder));
        return Task.CompletedTask;
    }

    public Task WriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(path))!);
        return File.WriteAllBytesAsync(Full(path), bytes, ct);
    }

    public Task ReplaceAsync(string tempPath, string path, CancellationToken ct)
    {
        File.Move(Full(tempPath), Full(path), overwrite: true);
        return Task.CompletedTask;
    }

    public Task MoveAsync(string path, string destination, CancellationToken ct)
    {
        File.Move(Full(path), Full(destination), overwrite: false);
        return Task.CompletedTask;
    }

    public async Task AppendAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Full(path))!);
        await using var stream = new FileStream(Full(path), FileMode.Append, FileAccess.Write);
        await stream.WriteAsync(bytes, ct);
    }
}
