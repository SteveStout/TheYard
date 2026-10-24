using System.Text.RegularExpressions;

namespace JobHunt.Application.Features.Merge;

/// <summary>
/// A correction to one existing row, fixed in place rather than by a correction row. The file is
/// named fix-&lt;Count&gt;-&lt;lane&gt;-&lt;slug&gt;.csv and holds the full corrected seven-column row; the
/// merger replaces that Count's row, stamps [FIX:file name] into Notes, and carries forward every
/// [TID:] and [FIX:] receipt the old row held, so earlier merges and fixes stay provable.
/// </summary>
public sealed partial class FixMerge(MergeContext context)
{
    public async Task RunAsync(string spool, CancellationToken ct)
    {
        foreach (MergeEntry file in (await context.Store.ListFilesAsync(spool, ct))
                     .Where(f => f.Name.StartsWith("fix-", StringComparison.OrdinalIgnoreCase)
                                 && f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            await FixAsync(spool, file.Name, ct);
        }
    }

    private async Task FixAsync(string spool, string name, CancellationToken ct)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string tag = $"[FIX:{stem}]";
        Match target = FixName().Match(stem);
        if (!target.Success)
        {
            await RejectAsync(spool, name, "name must be fix-<Count>-<lane>-<slug>.csv", ct);
            return;
        }
        string count = int.Parse(target.Groups[1].Value).ToString();
        if ((await context.ReadTextAsync(MergePaths.Master, ct)).Contains(tag, StringComparison.Ordinal))
        {
            await context.LogAsync($"already applied {tag}, moved to merged", ct);
            await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
            return;
        }

        List<string[]> given = (await context.ParseAsync(MergePaths.In(spool, name), ct))
            .Where(r => string.Concat(r).Trim().Length > 0)
            .ToList();
        if (given.Count > 0 && string.Equals(TrackerCsvText.Letters(given[0][0]), "Count", StringComparison.OrdinalIgnoreCase)) { given.RemoveAt(0); }
        if (given.Count != 1 || given[0].Length < 7)
        {
            await RejectAsync(spool, name, "needs exactly one 7-column row", ct);
            return;
        }
        string[] row = given[0][..7];
        if (TrackerCsvText.Digits(row[0]) != count)
        {
            await RejectAsync(spool, name, $"row Count {row[0]} is not {count}", ct);
            return;
        }
        row[0] = count;
        if (!row[6].Contains(tag, StringComparison.Ordinal)) { row[6] = (row[6].TrimEnd() + " " + tag).Trim(); }

        List<string[]> rows = await context.ParseAsync(MergePaths.Master, ct);
        int index = rows.FindIndex(1, r => r.Length > 0 && TrackerCsvText.Digits(r[0]) == count);
        if (index < 0)
        {
            await RejectAsync(spool, name, $"Count {count} not in master", ct);
            return;
        }
        string oldNotes = rows[index].Length >= 7 ? rows[index][6] : string.Empty;
        int carried = 0;
        foreach (Match receipt in Receipt().Matches(oldNotes))
        {
            if (row[6].Contains(receipt.Value, StringComparison.Ordinal)) { continue; }
            row[6] = (row[6].TrimEnd() + " " + receipt.Value).Trim();
            carried++;
        }
        if (carried > 0) { await context.LogAsync($"  {name}: carried {carried} existing receipt tag(s) forward onto Count {count}", ct); }
        rows[index] = row;

        string? error = await context.WriteMasterRowsAsync(rows, ct);
        if (error is not null)
        {
            await context.LogAsync($"FIX FAILED ({error}): {name}; left for the next pass", ct);
            return;
        }
        byte[] after = await context.ReadBytesAsync(MergePaths.Master, ct);
        if (!TrackerCsvText.Decode(after).Contains(tag, StringComparison.Ordinal))
        {
            await context.LogAsync($"FIX WRITTEN BUT TAG NOT FOUND: {name}; left for the next pass", ct);
            return;
        }
        await context.LogAsync($"FIXED Count {count} in place from {name}; master now {after.Length} B", ct);
        await context.MoveSafeAsync(spool, name, MergePaths.Merged(spool), ct);
    }

    private async Task RejectAsync(string spool, string name, string reason, CancellationToken ct)
    {
        await context.LogAsync($"REJECTED {name}: {reason}", ct);
        await context.MoveSafeAsync(spool, name, MergePaths.Rejected(spool), ct);
    }

    [GeneratedRegex("^fix-(\\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex FixName();

    [GeneratedRegex("\\[(?:TID|FIX):[^\\]]*\\]")]
    private static partial Regex Receipt();
}
