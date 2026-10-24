namespace JobHunt.Application.Features.Merge;

/// <summary>
/// One pass of the tracker merger: for each spool folder, new rows, then fixes, then moveouts,
/// the order the laptop's merger always ran them. It is the only writer of Job_Hunt_Tracker.csv
/// and Job_Hunt_Tracker_MOVED_OUT.csv on the share; lanes only ever drop files into a spool.
/// With appends on, the same pass then appends the lanes' .md fragments (TextAppendMerge), which
/// makes it the only writer of the shared .md files too; with them off the laptop's merger keeps those.
/// </summary>
public sealed class TrackerMerger(IMergeStore store, TimeProvider time)
{
    /// <summary>The append skips already logged, kept across passes so each is said once a process.</summary>
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);

    /// <summary>Runs one pass and returns how many spool files it found waiting.</summary>
    /// <param name="ct">Stops the pass.</param>
    /// <param name="appends">True to append the .md fragments after the rows.</param>
    public async Task<int> RunPassAsync(CancellationToken ct, bool appends = false)
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
        if (appends)
        {
            var text = new TextAppendMerge(context, _said);
            foreach (string root in TextAppendMerge.Roots)
            {
                await text.RunAsync(root, ct);
            }
        }
        return waiting;
    }

    /// <summary>A file a lane left for the merger: a row or fix file (.csv) or a moveout list; the merger's own files are not.</summary>
    public static bool IsSpoolFile(string name) =>
        name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("moveout-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
}
