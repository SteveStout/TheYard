namespace JobHunt.Functions.Merge;

/// <summary>
/// One pass of the tracker merger: for each spool folder, new rows, then fixes, then moveouts,
/// the order the laptop's merger always ran them. It is the only writer of Job_Hunt_Tracker.csv
/// and Job_Hunt_Tracker_MOVED_OUT.csv on the share; lanes only ever drop files into a spool.
/// Appends to the shared .md files are not here; the laptop's merger keeps those.
/// </summary>
public sealed class TrackerMerger(IMergeStore store, TimeProvider time)
{
    /// <summary>Runs one pass and returns how many spool files it found waiting.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        var context = new MergeContext(store, () => time.GetUtcNow());
        var rows = new RowMerge(context);
        var fixes = new FixMerge(context);
        var moves = new MoveOutMerge(context);
        int waiting = 0;
        foreach (string spool in MergePaths.Spools)
        {
            waiting += (await store.ListFilesAsync(spool, ct)).Count(f => IsSpoolFile(f.Name));
            await rows.RunAsync(spool, ct);
            await fixes.RunAsync(spool, ct);
            await moves.RunAsync(spool, ct);
        }
        return waiting;
    }

    /// <summary>A file a lane left for the merger: a row or fix file (.csv) or a moveout list; the merger's own files are not.</summary>
    public static bool IsSpoolFile(string name) =>
        name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("moveout-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
}
