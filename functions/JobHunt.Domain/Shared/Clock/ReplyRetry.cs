namespace JobHunt.Domain.Shared.Clock;

/// <summary>
/// Whether the day's reply run needs its second dispatch: true when no reply run folder of the day holds a queue. A
/// folder is one run, nightly/&lt;date&gt;/&lt;HHMM&gt;-reply-&lt;model&gt;; a dry run's folder ends in -dry and never counts,
/// because the morning's lanes never read a dry run's queue.
/// </summary>
public static class ReplyRetry
{
    /// <summary>The pattern a reply run's folder matches, at any slot.</summary>
    public const string Folders = "-reply-";

    /// <summary>The file the 6 AM messages lane reads out of the run's folder.</summary>
    public const string Queue = "linkedin-queue.md";

    /// <summary>Whether a folder name is a real (not dry) reply run's.</summary>
    /// <param name="folder">The folder's own name, without its parent.</param>
    public static bool IsRun(string folder) =>
        folder.Contains(Folders, StringComparison.Ordinal) && !folder.EndsWith("-dry", StringComparison.Ordinal);

    /// <summary>True when none of the day's reply runs wrote its queue.</summary>
    /// <param name="runs">Each reply run folder of the day with whether it holds the queue.</param>
    public static bool Needed(IEnumerable<(string Folder, bool HasQueue)> runs) =>
        !runs.Any(run => IsRun(run.Folder) && run.HasQueue);
}
