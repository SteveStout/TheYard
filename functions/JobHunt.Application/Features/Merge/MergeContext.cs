using System.Text;

namespace JobHunt.Application.Features.Merge;

/// <summary>
/// What every merge step shares: the store, the clock, merge.log, and the two safe ways the
/// master changes. A change is written to a temp file, read back and checked, and only then
/// renamed over the master, so a failure part way leaves the master as it was.
/// </summary>
public sealed class MergeContext(IMergeStore store, Func<DateTimeOffset> clock)
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public IMergeStore Store { get; } = store;

    public DateTime Now => CentralClock.Local(clock());

    public DateTimeOffset UtcNow => clock();

    public async Task LogAsync(string message, CancellationToken ct)
    {
        string line = $"{Now:yyyy-MM-dd HH:mm:ss} {message}\r\n";
        await Store.AppendAsync(MergePaths.Log, TrackerCsvText.Encode(line), ct);
    }

    public async Task<byte[]> ReadBytesAsync(string path, CancellationToken ct) =>
        await Store.ExistsAsync(path, ct) ? await Store.ReadAsync(path, ct) : [];

    public async Task<string> ReadTextAsync(string path, CancellationToken ct) =>
        TrackerCsvText.Decode(await ReadBytesAsync(path, ct));

    /// <summary>Parses a file, writing any malformed line to merge.log the way the laptop merger did.</summary>
    public async Task<List<string[]>> ParseAsync(string path, CancellationToken ct)
    {
        var errors = new List<string>();
        List<string[]> rows = TrackerCsvText.Parse(await ReadTextAsync(path, ct), errors.Add);
        foreach (string error in errors) { await LogAsync($"  parse error in {Path.GetFileName(path)} {error}", ct); }
        return rows;
    }

    /// <summary>Moves a spool file into a folder, adding "-dup" and the time when the name is taken there.</summary>
    public async Task MoveSafeAsync(string spool, string name, string folder, CancellationToken ct)
    {
        await Store.EnsureFolderAsync(folder, ct);
        string destination = MergePaths.In(folder, name);
        if (await Store.ExistsAsync(destination, ct))
        {
            destination = MergePaths.In(folder, $"{Path.GetFileNameWithoutExtension(name)}-dup{Now:HHmmss}{Path.GetExtension(name)}");
        }
        await Store.MoveAsync(MergePaths.In(spool, name), destination, ct);
    }

    /// <summary>
    /// Writes new bytes for a file through a temp file: the temp is read back and must match byte
    /// for byte before it is renamed over the target. Returns null on success, else what went wrong.
    /// </summary>
    public async Task<string?> ReplaceAsync(string path, byte[] bytes, CancellationToken ct)
    {
        string temp = MergePaths.Temp(path);
        await Store.WriteAsync(temp, bytes, ct);
        byte[] back = await Store.ReadAsync(temp, ct);
        if (!back.AsSpan().SequenceEqual(bytes)) { return $"temp read back {back.Length} B, wrote {bytes.Length} B"; }
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Store.ReplaceAsync(temp, path, ct);
                return null;
            }
            catch (Exception) when (attempt < 9)
            {
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                return $"{Path.GetFileName(path)} locked ({ex.GetType().Name})";
            }
        }
        return $"{Path.GetFileName(path)} locked";
    }

    /// <summary>
    /// Rewrites the master from parsed rows: the header line exactly as it stands, every other row
    /// re-quoted, the byte order mark kept as it was. The new text must parse back to exactly the
    /// intended fields before it replaces the master. Returns null on success, else the reason.
    /// </summary>
    public async Task<string?> WriteMasterRowsAsync(IReadOnlyList<string[]> rows, CancellationToken ct)
    {
        byte[] current = await ReadBytesAsync(MergePaths.Master, ct);
        string firstLine = TrackerCsvText.Decode(current).Split('\n', 2)[0].TrimEnd('\r');
        var text = new StringBuilder(firstLine).Append("\r\n");
        for (int i = 1; i < rows.Count; i++) { text.Append(TrackerCsvText.Line(rows[i])).Append("\r\n"); }

        List<string[]> back = TrackerCsvText.Parse(text.ToString());
        if (back.Count != rows.Count) { return $"row count {back.Count} vs {rows.Count}"; }
        for (int i = 1; i < rows.Count; i++)
        {
            if (back[i].Length != rows[i].Length) { return $"field count differs at row {i}"; }
            for (int k = 0; k < rows[i].Length; k++)
            {
                if (!string.Equals(back[i][k], rows[i][k], StringComparison.Ordinal)) { return $"field {k} differs at row {i}"; }
            }
        }
        byte[] body = TrackerCsvText.Encode(text.ToString());
        byte[] bytes = TrackerCsvText.HasBom(current) ? [.. Bom, .. body] : body;
        return await ReplaceAsync(MergePaths.Master, bytes, ct);
    }
}
