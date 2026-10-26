namespace JobHunt.Application.Features.Merge;

// #region tracker-mirror
/// <summary>The tracker's frame as the SQL copy holds it: its header record, its byte order mark, its last break, its rows and the file's hash.</summary>
/// <param name="Record">The header record as written.</param>
/// <param name="Bom">True when the file opens with a byte order mark.</param>
/// <param name="EndsWithBreak">True when the file ends with a CRLF.</param>
/// <param name="Rows">The rows under the header.</param>
/// <param name="FileHash">The whole file's SHA-256, lower-case hex.</param>
/// <param name="ParityAt">When the last parity check ran, or null before the first.</param>
public sealed record TrackerFrame(string Record, bool Bom, bool EndsWithBreak, int Rows, string FileHash, DateTimeOffset? ParityAt = null);

/// <summary>The SQL copy of the tracker (jobhunt.Tracker and jobhunt.TrackerHeader). The CSV stays the master.</summary>
public interface ITrackerTable
{
    /// <summary>The frame, or null before the first import.</summary>
    Task<TrackerFrame?> FrameAsync(CancellationToken ct);

    /// <summary>Every row's record hash by position, from 1.</summary>
    Task<IReadOnlyDictionary<int, string>> HashesAsync(CancellationToken ct);

    /// <summary>Writes the rows at <paramref name="positions"/>, drops any row past the file's last, and writes the frame, in one transaction.</summary>
    Task KeepAsync(TrackerFile file, IReadOnlyList<int> positions, string fileHash, CancellationToken ct);

    /// <summary>The file rebuilt from the copy, or null before the first import.</summary>
    Task<TrackerFile?> RebuildAsync(CancellationToken ct);

    /// <summary>Records the day's parity check on the frame.</summary>
    Task MarkParityAsync(bool same, DateTimeOffset at, CancellationToken ct);
}

/// <summary>What one pass of the copy did.</summary>
/// <param name="Rows">The CSV's rows under its header.</param>
/// <param name="Written">The rows written this pass; 0 when the copy already held the file.</param>
public sealed record TrackerCopied(int Rows, int Written);

/// <summary>What the parity check found.</summary>
/// <param name="Same">True when the file rebuilt from the copy is the CSV, byte for byte.</param>
/// <param name="CsvRows">The CSV's rows.</param>
/// <param name="SqlRows">The copy's rows.</param>
public sealed record TrackerParity(bool Same, int CsvRows, int SqlRows);

/// <summary>
/// Keeps the SQL copy of the tracker in step with the CSV master (PROMPT-JOBHUNT-TRACKER-SQL, rungs 1 and 2): the CSV
/// is read, cut into records, and only the rows whose hash changed are written; once a day the file is rebuilt from
/// the copy and compared with the CSV byte for byte. It never writes the CSV.
/// </summary>
/// <param name="store">The job-hunt folder, where the master is.</param>
/// <param name="table">The SQL copy.</param>
/// <param name="seen">What the last pass saw of the file, kept for the life of the host; null reads the file every pass.</param>
public sealed class TrackerMirror(IMergeStore store, ITrackerTable table, TrackerSeen? seen = null)
{
    /// <summary>
    /// Brings the copy in step with the master; nothing is written when the file's hash already matches. When the
    /// file's size and last write are what the last pass saw and the copy still holds the hash that pass read, the
    /// file is not read at all.
    /// </summary>
    /// <param name="ct">Stops the pass.</param>
    public async Task<TrackerCopied> CopyAsync(CancellationToken ct)
    {
        var stat = seen is null ? null : await store.StatAsync(MergePaths.Master, ct);
        var frame = await table.FrameAsync(ct);
        if (stat is not null && seen!.Unmoved(stat, frame?.FileHash) is { } unmovedRows)
        {
            return new TrackerCopied(unmovedRows, 0);
        }
        byte[] bytes = await store.ReadAsync(MergePaths.Master, ct);
        var file = TrackerFile.Split(bytes);
        string hash = TrackerFile.FileHash(bytes);
        int rows = file.Records.Count - 1;
        if (frame?.FileHash == hash)
        {
            Remember(stat, rows, hash);
            return new TrackerCopied(rows, 0);
        }
        var kept = await table.HashesAsync(ct);
        var changed = Enumerable.Range(1, rows)
            .Where(position => !kept.TryGetValue(position, out string? was) || was != TrackerFile.Hash(file.Records[position]))
            .ToList();
        await table.KeepAsync(file, changed, hash, ct);
        Remember(stat, rows, hash);
        return new TrackerCopied(rows, changed.Count);
    }

    // The properties were read before the content, so a write in between leaves a newer last-write time on the
    // share than the one remembered here, and the next pass reads the file again: the safe way to be wrong.
    private void Remember(MergeEntry? stat, int rows, string hash)
    {
        if (stat is not null)
        {
            seen?.Remember(stat, rows, hash);
        }
    }

    /// <summary>True when no parity check has run yet on this Central day: the first pass of a day checks the whole file.</summary>
    /// <param name="last">When the last check ran, or null.</param>
    /// <param name="now">Now.</param>
    public static bool ParityDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is not { } at || DateOnly.FromDateTime(CentralClock.Local(at)) != DateOnly.FromDateTime(CentralClock.Local(now));

    /// <summary>Rebuilds the file from the copy and compares it with the CSV, byte for byte, and records what it found.</summary>
    /// <param name="at">When the check runs.</param>
    /// <param name="ct">Stops the check.</param>
    public async Task<TrackerParity> ParityAsync(DateTimeOffset at, CancellationToken ct)
    {
        byte[] csv = await store.ReadAsync(MergePaths.Master, ct);
        var rebuilt = await table.RebuildAsync(ct);
        int csvRows = TrackerFile.Split(csv).Records.Count - 1;
        bool same = rebuilt is not null && rebuilt.ToBytes().AsSpan().SequenceEqual(csv);
        await table.MarkParityAsync(same, at, ct);
        return new TrackerParity(same, csvRows, rebuilt is null ? 0 : rebuilt.Records.Count - 1);
    }
}
// #endregion tracker-mirror
