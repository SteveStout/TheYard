namespace JobHunt.Application.Features.Merge;

// #region tracker-seen
/// <summary>
/// What the tracker copy's last pass saw of the master file: its size, its last write, its rows and its hash. The
/// Function keeps one for the life of the host, so a pass can tell from the file's properties alone that nothing
/// moved since, and skip reading 1.8 MB a minute. It starts empty, so the first pass after a restart reads the file.
/// </summary>
public sealed class TrackerSeen
{
    private readonly Lock _gate = new();
    private Seen? _last;

    /// <summary>
    /// The rows the file held when it was last read, when the file is the same size with the same last write and the
    /// copy still holds the hash that read found; otherwise null, and the pass reads the file.
    /// </summary>
    /// <param name="file">The file's properties now.</param>
    /// <param name="copyHash">The hash the SQL copy's frame holds now, or null before the first import.</param>
    public int? Unmoved(MergeEntry file, string? copyHash)
    {
        lock (_gate)
        {
            return _last is { } last && last.Length == file.Length && last.Written == file.LastWritten && copyHash is not null && last.Hash == copyHash
                ? last.Rows
                : null;
        }
    }

    /// <summary>Remembers the file's properties with what reading it found.</summary>
    /// <param name="file">The properties read before the content.</param>
    /// <param name="rows">The rows under the header.</param>
    /// <param name="hash">The whole file's SHA-256.</param>
    public void Remember(MergeEntry file, int rows, string hash)
    {
        lock (_gate)
        {
            _last = new Seen(file.Length, file.LastWritten, rows, hash);
        }
    }

    private sealed record Seen(long Length, DateTimeOffset Written, int Rows, string Hash);
}
// #endregion tracker-seen
