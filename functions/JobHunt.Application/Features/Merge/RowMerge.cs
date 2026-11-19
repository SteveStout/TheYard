using System.Text;

namespace JobHunt.Application.Features.Merge;

/// <summary>
/// New tracker rows. Each spool file holds rows of six columns (Person, Company, Communication,
/// Date, Phone/Email, Notes), or seven with a Count the merger ignores, with or without a header.
/// The merger gives them the next Counts after the highest in the master and the moved-out file,
/// stamps [TID:file name] into Notes so a lane can prove its row landed, and appends them.
/// A file whose tag is already in either file has merged before and is only filed away.
/// A row that repeats a header ("Stamp,Lane,Person,Company,...") or carries no Person, Company,
/// Communication or Date is never a tracker row: it is dropped, and a file left with none goes to
/// rejected-logged/ with its README row. Two such rows became Counts 3789 and 3795, from two replies1119 spool files.
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
        int artifacts = rows.RemoveAll(IsArtifact);
        if (rows.Count == 0)
        {
            string reason = artifacts == 0 ? "no data rows" : $"no data rows ({artifacts} header line(s) or empty row(s), never a tracker row)";
            await context.LogAsync($"REJECTED {name}: {reason}", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.RejectedLogged(spool), ct);
            await ReadmeRowAsync(spool, name, reason, ct);
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
        string dropped = artifacts > 0 ? $"; {artifacts} header line(s) or empty row(s) dropped" : string.Empty;
        await context.LogAsync($"MERGED {name} -> Count {next}{range} ({rows.Count} row(s)){dropped}; master now {after.Length} B", ct);
        await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
    }

    private static bool IsHeader(string cell, string word) =>
        string.Equals(TrackerCsvText.Letters(cell), word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The words a spool file's header line is made of, as letters only.</summary>
    private static readonly HashSet<string> HeaderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Count", "Stamp", "Lane", "Person", "Company", "Communication", "Channel", "Date", "PhoneEmail", "Phone", "Email",
        "Notes", "Subject", "RefCode", "Verified",
    };

    /// <summary>
    /// A row that is not a tracker row: a header line (three or more of its cells are header words, so a real row whose
    /// Communication reads "Email" is kept), or a row with Person, Company, Communication and Date all empty.
    /// </summary>
    public static bool IsArtifact(string[] row)
    {
        if (row.Count(cell => HeaderWords.Contains(TrackerCsvText.Letters(cell))) >= 3) { return true; }
        string[] source = row.Length >= 7 ? row[1..7] : row;
        return source.Take(4).All(cell => cell.Trim().Length == 0);
    }

    /// <summary>The file's row in rejected-logged/README.md, so a reader of the folder knows why it is there.</summary>
    private async Task ReadmeRowAsync(string spool, string name, string reason, CancellationToken ct)
    {
        string readme = MergePaths.In(MergePaths.RejectedLogged(spool), "README.md");
        string head = await context.Store.ExistsAsync(readme, ct)
            ? string.Empty
            : "# Rejected spool files, read and logged\n\n| File | Merger's reason | Why no fix |\n|---|---|---|\n";
        string row = $"| {name} | {context.Now:yyyy-MM-dd HH:mm:ss} \"{reason}\" | the merger: a header line or an empty row is never a tracker row, so nothing is lost |\n";
        await context.Store.AppendAsync(readme, TrackerCsvText.Encode(head + row), ct);
    }
}
