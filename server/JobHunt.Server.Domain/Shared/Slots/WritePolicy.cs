namespace JobHunt.Server.Domain.Shared.Slots;

/// <summary>
/// Where the program may write on the share: a run folder or the day's folder under nightly/, the runs table, and
/// the two merger inboxes it spools to. Everything else is refused before the disk is touched, so a tracker, a
/// state.md or a failure-log.md can never be written whole by this process, whatever a later change asks of it.
/// </summary>
public static class WritePolicy
{
    /// <summary>The one table the program keeps outside a day's folder: clean runs per lane.</summary>
    public const string RunsTable = "nightly/server6am-runs.tsv";

    private const string Tracker = "inbox/tracker-inbox/";
    private const string Registry = "inbox/append-inbox/state.md/";

    /// <summary>True when the program may write this path.</summary>
    /// <param name="relative">The path relative to the share's root, with forward slashes.</param>
    public static bool Allowed(string relative)
    {
        string path = relative.Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/'))
        {
            return false;
        }
        if (path == RunsTable)
        {
            return true;
        }
        if (path.StartsWith(Tracker, StringComparison.Ordinal))
        {
            return path.EndsWith(".csv", StringComparison.Ordinal) && !path[Tracker.Length..].Contains('/');
        }
        if (path.StartsWith(Registry, StringComparison.Ordinal))
        {
            return path.EndsWith(".md", StringComparison.Ordinal) && !path[Registry.Length..].Contains('/');
        }
        return IsDayFolder(path);
    }

    private static bool IsDayFolder(string path)
    {
        string[] parts = path.Split('/');
        return parts.Length >= 3 && parts[0] == "nightly" && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", out _);
    }
}
