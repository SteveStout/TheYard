using System.Text;

namespace JobHunt.Application.Features.Merge;

/// <summary>
/// New tracker rows. Each spool file holds rows of six columns (Person, Company, Communication,
/// Date, Phone/Email, Notes), or seven with a Count the merger ignores, with or without a header.
/// The merger gives them the next Counts after the highest in the master and the moved-out file,
/// stamps [TID:file name] into Notes so a lane can prove its row landed, and appends them.
/// A file whose tag is already in either file has merged before and is only filed away.
/// </summary>
public sealed class RowMerge(MergeContext context)
{
    public async Task RunAsync(string spool, CancellationToken ct)
    {
        await RequeueVanishedAsync(spool, ct);
        foreach (MergeEntry file in (await context.Store.ListFilesAsync(spool, ct))
                     .Where(f => f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                                 && !f.Name.StartsWith("fix-", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            await MergeFileAsync(spool, file.Name, ct);
        }
    }

    /// <summary>
    /// A file merged in the last 24 hours whose tag has vanished from both files means something
    /// replaced the master after the merge; the file goes back to the spool and merges again.
    /// </summary>
    private async Task RequeueVanishedAsync(string spool, CancellationToken ct)
    {
        string merged = MergePaths.Merged(spool);
        var recent = (await context.Store.ListFilesAsync(merged, ct))
            .Where(f => f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && f.LastWritten >= context.UtcNow.AddHours(-24))
            .ToList();
        if (recent.Count == 0) { return; }
        string master = await context.ReadTextAsync(MergePaths.Master, ct);
        string movedOut = await context.ReadTextAsync(MergePaths.MovedOut, ct);
        foreach (MergeEntry file in recent)
        {
            string stem = Path.GetFileNameWithoutExtension(file.Name);
            string tag = file.Name.StartsWith("fix-", StringComparison.OrdinalIgnoreCase) ? $"[FIX:{stem}]" : $"[TID:{stem}]";
            if (master.Contains(tag, StringComparison.Ordinal) || movedOut.Contains(tag, StringComparison.Ordinal)) { continue; }
            await context.LogAsync($"REVERTED: {tag} is gone from the master (master {TrackerCsvText.Encode(master).Length} B; the merger cannot tell what removed it); re-queued", ct);
            await context.Store.MoveAsync(MergePaths.In(merged, file.Name), MergePaths.In(spool, file.Name), ct);
        }
    }

    private async Task MergeFileAsync(string spool, string name, CancellationToken ct)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string tid = $"[TID:{stem}]";
        byte[] masterBytes = await context.ReadBytesAsync(MergePaths.Master, ct);
        string masterText = TrackerCsvText.Decode(masterBytes);
        string movedOutText = await context.ReadTextAsync(MergePaths.MovedOut, ct);
        if (masterText.Contains(tid, StringComparison.Ordinal) || movedOutText.Contains(tid, StringComparison.Ordinal))
        {
            await context.LogAsync($"already present {tid}, moved to merged", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
            return;
        }

        List<string[]> rows = (await context.ParseAsync(MergePaths.In(spool, name), ct))
            .Where(r => string.Concat(r).Trim().Length > 0)
            .ToList();
        if (rows.Count > 0 && (IsHeader(rows[0][0], "Count") || IsHeader(rows[0][0], "Person"))) { rows.RemoveAt(0); }
        if (rows.Count == 0)
        {
            await context.LogAsync($"REJECTED {name}: no data rows", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Rejected(spool), ct);
            return;
        }

        int next = TrackerCsvText.MaxCount(TrackerCsvText.Parse(masterText).Concat(TrackerCsvText.Parse(movedOutText))) + 1;
        var added = new StringBuilder();
        if (!masterText.EndsWith('\n')) { added.Append("\r\n"); }
        for (int i = 0; i < rows.Count; i++)
        {
            string[] source = rows[i].Length >= 7 ? rows[i][1..7] : rows[i];
            var body = new string[6];
            for (int k = 0; k < 6; k++) { body[k] = k < source.Length ? source[k] : string.Empty; }
            if (!body[5].Contains(tid, StringComparison.Ordinal)) { body[5] = (body[5].TrimEnd() + " " + tid).Trim(); }
            added.Append((next + i).ToString()).Append(',').Append(TrackerCsvText.Line(body)).Append("\r\n");
        }

        byte[] bytes = [.. masterBytes, .. TrackerCsvText.Encode(added.ToString())];
        string? error = await context.ReplaceAsync(MergePaths.Master, bytes, ct);
        if (error is not null)
        {
            await context.LogAsync($"APPEND FAILED ({error}): {name}; left in the spool for the next pass", ct);
            return;
        }
        byte[] after = await context.ReadBytesAsync(MergePaths.Master, ct);
        if (!TrackerCsvText.Decode(after).Contains(tid, StringComparison.Ordinal))
        {
            await context.LogAsync($"APPENDED BUT NOT FOUND ON RE-READ: {name}; left in the spool for the next pass", ct);
            return;
        }
        string range = rows.Count > 1 ? $"..{next + rows.Count - 1}" : string.Empty;
        await context.LogAsync($"MERGED {name} -> Count {next}{range} ({rows.Count} row(s)); master now {after.Length} B", ct);
        await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
    }

    private static bool IsHeader(string cell, string word) =>
        string.Equals(TrackerCsvText.Letters(cell), word, StringComparison.OrdinalIgnoreCase);
}
