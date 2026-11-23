using System.Globalization;

namespace JobHunt.Server.Application.Features.Archive;

/// <summary>What one archive run was asked to do.</summary>
/// <param name="Dry">True to name the units and touch nothing: it reads names only.</param>
/// <param name="Unit">One unit by its path (mentor/logs, nightly/&lt;day&gt; or dispatch/done/&lt;day&gt;), or null for the daily pass.</param>
public sealed record ArchiveOptions(bool Dry, string? Unit)
{
    /// <summary>The options, or null for anything but archive [--dry-run] [--unit &lt;path&gt;].</summary>
    /// <param name="args">The command line, the verb first.</param>
    public static ArchiveOptions? Parse(IReadOnlyList<string> args)
    {
        bool dry = false;
        string? unit = null;
        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--dry-run" when !dry:
                    dry = true;
                    break;
                case "--unit" when unit is null && i + 1 < args.Count:
                    unit = args[++i].Replace('\\', '/').Trim('/');
                    break;
                default:
                    return null;
            }
        }
        return new ArchiveOptions(dry, unit);
    }
}

/// <summary>
/// The archive job (ADR: Old folders are zipped, not kept): each unit old enough is zipped, the zip reopened and every
/// entry's length and CRC-32 held to a second read of its source, one line written to ARCHIVE-LOG.tsv, and only then
/// the originals moved by one rename under _to_delete/datamove1123. A zip that does not verify is renamed aside and the
/// source stays where it is. Nothing is deleted. It does nothing while a cost1123 row is open on state.md, because that
/// lane's diagnostic log is reading the share. It refuses a nightly day with an unsettled run or one a live document
/// names, and its last line names each of those and any folder it moved that has come back.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="archiver">The zip, verify and move.</param>
/// <param name="time">The clock.</param>
public sealed class ArchiveRun(IShare share, IArchiver archiver, TimeProvider time)
{
    /// <summary>The lane whose open row holds the job.</summary>
    public const string Holder = "cost1123";

    /// <summary>A holder's row older than this no longer holds, so a lane that died cannot stop the job for good.</summary>
    public static readonly TimeSpan HolderCeiling = TimeSpan.FromHours(48);

    private const int TailBytes = 256 * 1024;

    /// <summary>One run, and the lines it prints; the exit is 1 when a zip did not verify, 2 for a unit it does not know, 0 for one it skipped.</summary>
    /// <param name="options">The options.</param>
    /// <param name="ct">Stops the run.</param>
    public async Task<(int Exit, string Output)> ExecuteAsync(ArchiveOptions options, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        string stamp = RunSlot.Local(now).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (RegistryTail.IsOpen(await share.TailAsync("state.md", TailBytes, ct), Holder, now, HolderCeiling))
        {
            return (0, $"archive {stamp} Central: held, a {Holder} row is open on state.md and its diagnostic log is reading the share; nothing listed, nothing moved");
        }
        var plan = new ArchivePlan(share, now);
        IReadOnlyList<ArchiveUnit> units;
        if (options.Unit is { } named)
        {
            if (await plan.NamedAsync(named, ct) is not { } one)
            {
                return plan.Lines.Count + plan.Refused.Count > 0
                    ? (0, string.Join('\n', [.. plan.Lines, $"archive {stamp} Central: {named} not archived{(plan.Refused.Count == 0 ? string.Empty : $", {plan.Refused[0]}")}"]))
                    : (2, $"archive {stamp} Central: {named} is not a unit this job takes (mentor/logs, nightly/<day> or dispatch/done/<day>)");
            }
            units = [one];
        }
        else
        {
            units = await plan.DailyAsync(options.Dry, ct);
        }
        if (options.Dry)
        {
            return (0, string.Join('\n', [.. units.Select(unit => $"would archive {unit.Label} -> {ArchiveUnits.ZipPath(unit)}"), .. plan.Lines, $"archive {stamp} Central, dry run: {units.Count} units named, {plan.Calls} calls, nothing zipped or moved"]));
        }
        var lines = new List<string>(plan.Lines);
        int archived = 0, failed = 0, left = 0;
        foreach (ArchiveUnit unit in units)
        {
            int files = await archiver.CountAsync(unit, ct);
            if (files == 0)
            {
                lines.Add($"skipped {unit.Label}: it is gone or holds no file");
                continue;
            }
            if (options.Unit is null && plan.Calls + archiver.Calls + (3 * files) + 8 > ArchiveUnits.MostCalls)
            {
                left++;
                lines.Add($"left for tomorrow: {unit.Label}, {files} files would pass the run's {ArchiveUnits.MostCalls} calls");
                continue;
            }
            (bool ok, string line) = await ArchiveAsync(unit, now, ct);
            archived += ok ? 1 : 0;
            failed += ok ? 0 : 1;
            lines.Add(line);
        }
        string? back = await plan.CameBackAsync(ct);
        string summary = $"archive {stamp} Central: {archived} archived, {failed} failed, {plan.Refused.Count} refused, {left} left for tomorrow, {plan.Calls + archiver.Calls} calls"
            + (back is null ? string.Empty : $". Came back: {back}")
            + (plan.Refused.Count == 0 ? string.Empty : $". Not archived: {string.Join("; ", plan.Refused)}");
        lines.Add(summary);
        if (back is not null || plan.Refused.Count > 0 || failed > 0)
        {
            await share.WriteNewAsync($"inbox/append-inbox/state.md/{RunSlot.Stamp(now)}-jobhunt-archive-note.md", $"## LANE: jobhunt-archive NOTE {RunSlot.Local(now).ToString("HH:mm 'CST' ddd M/d", CultureInfo.InvariantCulture)}: {summary} | NOTE |\n", ct);
        }
        return (failed > 0 ? 1 : 0, string.Join('\n', lines));
    }

    // #region archive-one-unit
    private async Task<(bool Ok, string Line)> ArchiveAsync(ArchiveUnit unit, DateTimeOffset now, CancellationToken ct)
    {
        ArchiveZip zip = await archiver.ZipAsync(unit, ArchiveUnits.ZipPath(unit), ct);
        IReadOnlyList<ArchiveEntry> inZip = await archiver.OpenAsync(zip.Zip, ct);
        IReadOnlyList<ArchiveEntry> source = await archiver.ReadAsync(unit, ct);
        if (Mismatch(inZip, source) is { } why)
        {
            string aside = await archiver.FailAsync(zip.Zip, ct);
            return (false, $"failed {unit.Label}: {why}; the zip is now {aside} and the source is where it was");
        }
        if (!await share.ExistsAsync(ArchiveUnits.LogPath, ct))
        {
            await share.AppendAsync(ArchiveUnits.LogPath, ArchiveUnits.LogHeader, ct);
        }
        string target = ArchiveUnits.Target(unit);
        await share.AppendAsync(ArchiveUnits.LogPath, ArchiveUnits.LogLine(now, unit, source.Count, zip, unit.IsFiles ? $"{target}/" : $"{target}/{unit.Name}"), ct);
        string moved = await archiver.MoveAsync(unit, target, ct);
        if (unit.Source == ArchiveUnits.Logs)
        {
            await share.WriteAsync(WritePolicy.LogsMoved, ArchiveUnits.MovedNote(now, zip, moved, source.Count), ct);
        }
        return (true, $"archived {unit.Label}: {source.Count} files, {zip.Bytes} bytes in {zip.Zip} (sha256 {zip.Sha256[..12]}), originals now {moved}");
    }

    // #endregion archive-one-unit

    /// <summary>Why the zip does not hold exactly the source as read a second time, or null when it does.</summary>
    private static string? Mismatch(IReadOnlyList<ArchiveEntry> inZip, IReadOnlyList<ArchiveEntry> source)
    {
        if (inZip.Count != source.Count)
        {
            return $"the zip holds {inZip.Count} entries and the source {source.Count} files";
        }
        var byPath = source.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (ArchiveEntry entry in inZip)
        {
            if (!byPath.TryGetValue(entry.Path, out var read))
            {
                return $"{entry.Path} is in the zip and not in the source";
            }
            if (read.Length != entry.Length || read.Crc != entry.Crc)
            {
                return $"{entry.Path} reads {read.Length} bytes, CRC {read.Crc:x8}, and the zip holds {entry.Length} bytes, CRC {entry.Crc:x8}";
            }
        }
        return null;
    }
}
