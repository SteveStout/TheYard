using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Functions.Merge;

/// <summary>
/// Moving rows out of the tracker into Job_Hunt_Tracker_MOVED_OUT.csv. A moveout-&lt;lane&gt;-&lt;slug&gt;.txt
/// lists Counts and must carry a line "# approved-by: Steve ..." or it is refused untouched, so no
/// lane can pull rows on its own. Rows are copied to the moved-out file first and removed from the
/// master second, so a failure between the two can never lose a row; nothing is deleted.
/// </summary>
public sealed partial class MoveOutMerge(MergeContext context)
{
    public async Task RunAsync(string spool, CancellationToken ct)
    {
        foreach (MergeEntry file in (await context.Store.ListFilesAsync(spool, ct))
                     .Where(f => f.Name.StartsWith("moveout-", StringComparison.OrdinalIgnoreCase)
                                 && f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            await MoveAsync(spool, file.Name, ct);
        }
    }

    private async Task MoveAsync(string spool, string name, CancellationToken ct)
    {
        string tag = $"[MOVED:{Path.GetFileNameWithoutExtension(name)}]";
        string body = await context.ReadTextAsync(MergePaths.In(spool, name), ct);
        if (!Approved().IsMatch(body))
        {
            await context.LogAsync($"REJECTED {name}: no '# approved-by: Steve' line; moves need his approval", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Rejected(spool), ct);
            return;
        }
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in body.Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) { continue; }
            foreach (string part in Separators().Split(text))
            {
                if (int.TryParse(part, out int n)) { wanted.Add(n.ToString()); }
            }
        }
        if (wanted.Count == 0)
        {
            await context.LogAsync($"REJECTED {name}: no Counts listed", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Rejected(spool), ct);
            return;
        }

        List<string[]> rows = await context.ParseAsync(MergePaths.Master, ct);
        var keep = new List<string[]> { rows[0] };
        var moving = new List<string[]>();
        foreach (string[] row in rows.Skip(1))
        {
            (wanted.Contains(TrackerCsvText.Digits(row.Length > 0 ? row[0] : null)) ? moving : keep).Add(row);
        }
        byte[] archiveBytes = await context.ReadBytesAsync(MergePaths.MovedOut, ct);
        string archive = TrackerCsvText.Decode(archiveBytes);
        var notAnywhere = wanted
            .Where(k => !moving.Any(r => TrackerCsvText.Digits(r[0]) == k))
            .Where(k => !Regex.IsMatch(archive, $"(?m)^\"?{k}\"?,"))
            .ToList();
        if (notAnywhere.Count > 0) { await context.LogAsync($"  {name}: Count(s) not in master or MOVED_OUT, skipped: {string.Join(',', notAnywhere)}", ct); }
        if (moving.Count == 0)
        {
            await context.LogAsync($"already moved {tag} (nothing left to move), moved to merged", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
            return;
        }

        var added = new StringBuilder();
        bool archiveExists = await context.Store.ExistsAsync(MergePaths.MovedOut, ct);
        if (!archiveExists) { added.Append(MergePaths.MovedOutHeader).Append("\r\n"); }
        else if (!archive.EndsWith('\n')) { added.Append("\r\n"); }
        foreach (string[] row in moving)
        {
            var cells = row.ToList();
            while (cells.Count < 7) { cells.Add(string.Empty); }
            if (!cells[6].Contains(tag, StringComparison.Ordinal)) { cells[6] = (cells[6].TrimEnd() + " " + tag).Trim(); }
            added.Append(TrackerCsvText.Line(cells)).Append("\r\n");
        }
        string? archived = await context.ReplaceAsync(MergePaths.MovedOut, [.. archiveBytes, .. TrackerCsvText.Encode(added.ToString())], ct);
        if (archived is not null)
        {
            await context.LogAsync($"MOVE FAILED (archive {archived}): {name}; left for the next pass", ct);
            return;
        }
        if (!(await context.ReadTextAsync(MergePaths.MovedOut, ct)).Contains(tag, StringComparison.Ordinal))
        {
            await context.LogAsync($"MOVE FAILED (archive re-read): {name}; left for the next pass", ct);
            return;
        }

        string? error = await context.WriteMasterRowsAsync(keep, ct);
        if (error is not null)
        {
            await context.LogAsync($"MOVE: archived but master rewrite failed ({error}): {name}; left for the next pass", ct);
            return;
        }
        int size = (await context.ReadBytesAsync(MergePaths.Master, ct)).Length;
        await context.LogAsync($"MOVED OUT {moving.Count} row(s) from {name} to {MergePaths.MovedOut}; master now {size} B", ct);
        await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
    }

    [GeneratedRegex("(?im)^\\s*#\\s*approved-by:\\s*Steve\\b")]
    private static partial Regex Approved();

    [GeneratedRegex("[,\\s]+")]
    private static partial Regex Separators();
}
