namespace JobHunt.Server.Domain.Shared.Slots;

/// <summary>
/// Where the program may write on the share: a run folder or the day's folder under nightly/, the runs table, the
/// two merger inboxes it spools to, the feed's one queue file a day in linkedin-proof/, and the network log's LinkedIn
/// spool: its TSV files and thread texts under networklog/linkedin-inbox/, its high-water mark and its backlog floor. Everything else is refused before the disk is touched, so a tracker, a
/// state.md or a failure-log.md can never be written whole by this process, whatever a later change asks of it.
/// </summary>
public static class WritePolicy
{
    /// <summary>The one table the program keeps outside a day's folder: clean runs per lane.</summary>
    public const string RunsTable = "nightly/server6am-runs.tsv";

    private const string Tracker = "inbox/tracker-inbox/";
    private const string Registry = "inbox/append-inbox/state.md/";
    private const string FeedQueue = "linkedin-proof/QUEUE-hotmgr-";

    /// <summary>Where the network lane's TSV files go for the Function to read.</summary>
    public const string LinkedInInbox = "networklog/linkedin-inbox/";

    /// <summary>Where each thread's text goes, by thread id.</summary>
    public const string LinkedInText = "networklog/linkedin-inbox/text/";

    /// <summary>The newest LinkedIn exchange the network lane has read, so the next run reads back only to it.</summary>
    public const string HighWater = "networklog/linkedin-high-water.txt";

    /// <summary>The oldest LinkedIn exchange the backlog has reached, or "done" once it reached its window's end.</summary>
    public const string Floor = "networklog/linkedin-floor.txt";

    /// <summary>True when the program may write this path.</summary>
    /// <param name="relative">The path relative to the share's root, with forward slashes.</param>
    public static bool Allowed(string relative)
    {
        string path = relative.Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/'))
        {
            return false;
        }
        if (path == RunsTable || path == HighWater || path == Floor)
        {
            return true;
        }
        if (path.StartsWith(LinkedInText, StringComparison.Ordinal))
        {
            return path.EndsWith(".txt", StringComparison.Ordinal) && !path[LinkedInText.Length..].Contains('/');
        }
        if (path.StartsWith(LinkedInInbox, StringComparison.Ordinal))
        {
            return path.EndsWith(".tsv", StringComparison.Ordinal) && !path[LinkedInInbox.Length..].Contains('/');
        }
        if (path.StartsWith(Tracker, StringComparison.Ordinal))
        {
            return path.EndsWith(".csv", StringComparison.Ordinal) && !path[Tracker.Length..].Contains('/');
        }
        if (path.StartsWith(Registry, StringComparison.Ordinal))
        {
            return path.EndsWith(".md", StringComparison.Ordinal) && !path[Registry.Length..].Contains('/');
        }
        if (path.StartsWith(FeedQueue, StringComparison.Ordinal))
        {
            return DateOnly.TryParseExact(path[FeedQueue.Length..].Replace(".md", string.Empty, StringComparison.Ordinal), "yyyy-MM-dd", out _) && path.EndsWith(".md", StringComparison.Ordinal);
        }
        return IsDayFolder(path);
    }

    private static bool IsDayFolder(string path)
    {
        string[] parts = path.Split('/');
        return parts.Length >= 3 && parts[0] == "nightly" && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", out _);
    }
}
