using System.Globalization;

namespace JobHunt.Application.Features.Merge;

// #region tracker-seen
/// <summary>
/// What the tracker copy's last pass saw of the master file: its size, its last write, its rows and its hash, and when the
/// copy's day check last ran. The Function keeps one for the life of the host, so a pass can tell from the file's
/// properties alone that nothing moved since, and skip reading 1.8 MB a minute. A new instance starts empty and reads the
/// copy of it the last pass left on the share (ADR-111, the addendum on the quiet pass), so it need not ask SQL either.
/// </summary>
public sealed class TrackerSeen
{
    private readonly Lock _gate = new();
    private Seen? _last;

    /// <summary>True when nothing is remembered yet: a new instance, before it has read the share's copy or made a pass.</summary>
    public bool Empty
    {
        get
        {
            lock (_gate)
            {
                return _last is null;
            }
        }
    }

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

    /// <summary>
    /// True when the copy can be left alone without asking SQL: the file is the same size with the same last write as the
    /// last copy saw, and the day's check already ran on this Central day.
    /// </summary>
    /// <param name="file">The file's properties now.</param>
    /// <param name="now">Now.</param>
    public bool Quiet(MergeEntry file, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _last is { } last && last.Length == file.Length && last.Written == file.LastWritten && !TrackerMirror.ParityDue(last.ParityAt, now);
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
            _last = new Seen(file.Length, file.LastWritten, rows, hash, _last?.ParityAt);
        }
    }

    /// <summary>Remembers when the copy's day check last ran, as the copy's frame says or as this pass ran it.</summary>
    /// <param name="at">When it ran, or null before the first.</param>
    public void Checked(DateTimeOffset? at)
    {
        lock (_gate)
        {
            if (_last is { } last)
            {
                _last = last with { ParityAt = at };
            }
        }
    }

    /// <summary>What is remembered as one line of text for the share, or null when nothing is: size, last write, rows, hash and the day check, by tabs.</summary>
    public string? Text()
    {
        lock (_gate)
        {
            return _last is not { } last ? null
                : string.Join('\t', last.Length.ToString(CultureInfo.InvariantCulture), last.Written.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    last.Rows.ToString(CultureInfo.InvariantCulture), last.Hash, last.ParityAt?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "");
        }
    }

    /// <summary>Takes up what the share holds when nothing is remembered yet; a line that does not read is left alone, and the pass asks SQL.</summary>
    /// <param name="text">The share's line.</param>
    public void Load(string text)
    {
        string[] parts = text.Trim().Split('\t');
        if (parts.Length != 5
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long length)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long written)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rows)
            || parts[3].Length == 0
            || !InRange(written))
        {
            return;
        }
        DateTimeOffset? parity = long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) && InRange(ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        lock (_gate)
        {
            _last ??= new Seen(length, new DateTimeOffset(written, TimeSpan.Zero), rows, parts[3], parity);
        }
    }

    /// <summary>True for ticks an instant can hold; a line carrying any other is not read.</summary>
    private static bool InRange(long ticks) => ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks;

    private sealed record Seen(long Length, DateTimeOffset Written, int Rows, string Hash, DateTimeOffset? ParityAt);
}
// #endregion tracker-seen
