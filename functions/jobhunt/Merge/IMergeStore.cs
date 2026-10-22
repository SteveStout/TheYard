namespace JobHunt.Functions.Merge;

/// <summary>
/// The files the tracker merger reads and writes, addressed by paths relative to the job-hunt
/// folder with forward slashes ("tracker-inbox/merged/x.csv"). The merger's rules only talk to
/// this port; the Azure Files share and a plain folder on disk (for tests) are its two adapters.
/// </summary>
public interface IMergeStore
{
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    Task<byte[]> ReadAsync(string path, CancellationToken ct);

    /// <summary>The files directly in a folder; an absent folder lists as empty.</summary>
    Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct);

    /// <summary>The names of the folders directly in a folder; an absent folder lists as empty.</summary>
    Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct);

    Task EnsureFolderAsync(string folder, CancellationToken ct);

    /// <summary>Creates the file, or replaces its whole content.</summary>
    Task WriteAsync(string path, byte[] bytes, CancellationToken ct);

    /// <summary>Renames a written temp file over the target in one step, so a reader never sees half a file.</summary>
    Task ReplaceAsync(string tempPath, string path, CancellationToken ct);

    /// <summary>Renames a file to a new path; fails when the new path is taken.</summary>
    Task MoveAsync(string path, string destination, CancellationToken ct);

    /// <summary>Adds bytes to the end of a file, creating it when absent.</summary>
    Task AppendAsync(string path, byte[] bytes, CancellationToken ct);
}

/// <summary>One file in a listing: its name, when its content was last written, and its size.</summary>
public sealed record MergeEntry(string Name, DateTimeOffset LastWritten, long Length);
