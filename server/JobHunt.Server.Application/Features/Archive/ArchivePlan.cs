namespace JobHunt.Server.Application.Features.Archive;

/// <summary>
/// Which units one run takes, read from the five folders one level each and, for a nightly day, from inside that day
/// only: the oldest first, at most five of a kind. A nightly day is refused while any of its runs is unsettled, the
/// same two tests the lane's crash hold applies, or while a live document names it, because archiving it would lift a
/// hold that never runs out on its own or move a folder a lane still reads. Every call to the share is counted.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="now">The instant.</param>
internal sealed class ArchivePlan(IShare share, DateTimeOffset now)
{
    private readonly DateOnly today = RunSlot.Day(now);

    /// <summary>Lines for the run's output: units skipped and units waiting for a later run.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>Each nightly day older than fourteen days this run could not take, with why.</summary>
    public List<string> Refused { get; } = [];

    /// <summary>The calls this plan made to the share.</summary>
    public int Calls { get; private set; }

    /// <summary>The one unit a path names, or null with a line saying why not.</summary>
    /// <param name="path">mentor/logs, nightly/&lt;day&gt; or dispatch/done/&lt;day&gt;.</param>
    /// <param name="ct">Stops the read.</param>
    public async Task<ArchiveUnit?> NamedAsync(string path, CancellationToken ct)
    {
        if (path == ArchiveUnits.Logs)
        {
            if ((await FoldersAsync("mentor", "logs", ct)).Count == 0)
            {
                Lines.Add("skipped mentor/logs: it is not there");
                return null;
            }
            if (await ExistsAsync(WritePolicy.LogsMoved, ct))
            {
                Lines.Add("skipped mentor/logs: it holds MOVED.md, so it was archived already");
                return null;
            }
            return ArchiveUnits.LogsUnit;
        }
        foreach (string parent in new[] { ArchiveUnits.Nightly, ArchiveUnits.Done })
        {
            if (!path.StartsWith(parent + "/", StringComparison.Ordinal) || ArchiveUnits.Day(path[(parent.Length + 1)..]) is not { } day)
            {
                continue;
            }
            if (!ArchiveUnits.OldDay(day, today))
            {
                Lines.Add($"skipped {path}: {today.DayNumber - day.DayNumber} days old, and a day waits more than {ArchiveUnits.DayAge}");
                return null;
            }
            if ((await FoldersAsync(parent, path[(parent.Length + 1)..], ct)).Count == 0)
            {
                Lines.Add($"skipped {path}: it is not there");
                return null;
            }
            if (parent == ArchiveUnits.Nightly && await RefusalAsync(day, await NamedDaysAsync(ct), ct) is { } why)
            {
                Refused.Add($"{path} ({why})");
                return null;
            }
            return ArchiveUnits.DayUnit(parent, day);
        }
        return null;
    }

    /// <summary>The daily pass's units, oldest first, at most five of a kind; a dry pass reads names only and refuses nothing.</summary>
    /// <param name="dry">True for a dry pass.</param>
    /// <param name="ct">Stops the read.</param>
    public async Task<IReadOnlyList<ArchiveUnit>> DailyAsync(bool dry, CancellationToken ct)
    {
        var units = new List<ArchiveUnit>();
        IReadOnlyList<DateOnly> nightly = await OldDaysAsync(ArchiveUnits.Nightly, ct);
        IReadOnlyDictionary<DateOnly, string> named = dry ? new Dictionary<DateOnly, string>() : await NamedDaysAsync(ct);
        int taken = 0, waiting = 0;
        foreach (DateOnly day in nightly)
        {
            if (taken == ArchiveUnits.PerKind)
            {
                waiting++;
                continue;
            }
            if (!dry && await RefusalAsync(day, named, ct) is { } why)
            {
                Refused.Add($"{RunSlot.DayFolder(day)} ({why})");
                continue;
            }
            units.Add(ArchiveUnits.DayUnit(ArchiveUnits.Nightly, day));
            taken++;
        }
        Waiting("nightly days", waiting);
        IReadOnlyList<DateOnly> done = await OldDaysAsync(ArchiveUnits.Done, ct);
        units.AddRange(done.Take(ArchiveUnits.PerKind).Select(day => ArchiveUnits.DayUnit(ArchiveUnits.Done, day)));
        Waiting("dispatch done days", done.Count - ArchiveUnits.PerKind);
        IReadOnlyList<ShareFile> locks = await ListAsync(ArchiveUnits.Locks, ct);
        IReadOnlyList<ArchiveUnit> lockMonths = ArchiveUnits.LockUnits(locks.Select(f => Name(f.Path)), now);
        units.AddRange(lockMonths.Take(ArchiveUnits.PerKind));
        Waiting("lock months", lockMonths.Count - ArchiveUnits.PerKind);
        IReadOnlyList<ShareFile> merged = await ListAsync(ArchiveUnits.Merged, ct);
        IReadOnlyList<ArchiveUnit> mergedMonths = ArchiveUnits.MergedUnits(merged.Select(f => (Name(f.Path), f.Changed)), now);
        units.AddRange(mergedMonths.Take(ArchiveUnits.PerKind));
        Waiting("link-token months", mergedMonths.Count - ArchiveUnits.PerKind);
        return units;
    }

    /// <summary>A folder the job moved that holds something again, in words, or null: mentor/logs with more than MOVED.md.</summary>
    /// <param name="ct">Stops the read.</param>
    public async Task<string?> CameBackAsync(CancellationToken ct)
    {
        if (!await ExistsAsync(WritePolicy.LogsMoved, ct))
        {
            return null;
        }
        var beside = (await ListAsync(ArchiveUnits.Logs, ct)).Select(f => Name(f.Path)).Where(name => name != "MOVED.md")
            .Concat((await FoldersAsync(ArchiveUnits.Logs, "*", ct)).Select(Name)).Order(StringComparer.Ordinal).ToList();
        return beside.Count == 0 ? null : $"mentor/logs holds {beside.Count} entries beside MOVED.md, the first {beside[0]}";
    }

    private void Waiting(string what, int count)
    {
        if (count > 0)
        {
            Lines.Add($"waiting: {count} more {what} for the next runs, {ArchiveUnits.PerKind} a run");
        }
    }

    private async Task<IReadOnlyList<DateOnly>> OldDaysAsync(string parent, CancellationToken ct) =>
        [.. (await FoldersAsync(parent, "20*", ct)).Select(path => ArchiveUnits.Day(Name(path))).OfType<DateOnly>().Where(day => ArchiveUnits.OldDay(day, today)).Order()];

    /// <summary>Every day a live document names, with the first document that names it: nightly's own .md files that are not backups, and START-HERE.md.</summary>
    private async Task<IReadOnlyDictionary<DateOnly, string>> NamedDaysAsync(CancellationToken ct)
    {
        var named = new Dictionary<DateOnly, string>();
        IEnumerable<string> documents = (await ListAsync(ArchiveUnits.Nightly, ct)).Select(f => f.Path).Where(path => ArchiveUnits.LiveDocument(Name(path))).Order(StringComparer.Ordinal).Append("START-HERE.md");
        foreach (string document in documents)
        {
            if (!await ExistsAsync(document, ct))
            {
                continue;
            }
            Calls++;
            foreach (DateOnly day in ArchiveUnits.NamedDays(await share.ReadAsync(document, ct)))
            {
                named.TryAdd(day, document);
            }
        }
        return named;
    }

    // #region archive-refusal
    /// <summary>Why a nightly day may not be archived, or null: a document names it, or one of its runs is unsettled.</summary>
    private async Task<string?> RefusalAsync(DateOnly day, IReadOnlyDictionary<DateOnly, string> named, CancellationToken ct)
    {
        if (named.TryGetValue(day, out string? document))
        {
            return $"{document} names it";
        }
        foreach (string run in await FoldersAsync(RunSlot.DayFolder(day), "*", ct))
        {
            if (run.EndsWith("-dry", StringComparison.Ordinal))
            {
                continue;
            }
            string? note = null;
            if (await ExistsAsync($"{run}/DONE.md", ct))
            {
                Calls++;
                note = await share.ReadAsync($"{run}/DONE.md", ct);
            }
            if (note is not null && RunSettlement.Unsettled(note))
            {
                return $"{Name(run)}/DONE.md carries a defect line or an unwritten tracker row";
            }
            if (note is not null && RunSettlement.Held(note))
            {
                return $"{Name(run)}/DONE.md says HELD";
            }
            if (note is null && await ExistsAsync($"{run}/progress.tsv", ct))
            {
                return $"{Name(run)}/progress.tsv has no DONE note";
            }
        }
        return null;
    }
    // #endregion archive-refusal

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private async Task<IReadOnlyList<string>> FoldersAsync(string folder, string pattern, CancellationToken ct)
    {
        Calls++;
        return await share.FoldersAsync(folder, pattern, ct);
    }

    private async Task<IReadOnlyList<ShareFile>> ListAsync(string folder, CancellationToken ct)
    {
        Calls++;
        return await share.ListAsync(folder, "*", ct);
    }

    private async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        Calls++;
        return await share.ExistsAsync(path, ct);
    }
}
