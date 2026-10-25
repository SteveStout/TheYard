namespace JobHunt.Domain.Shared.Tracker;

/// <summary>
/// Where the tracker merger finds its work. Laptop lanes' row files arrive in tracker-inbox/ (the
/// laptop's sync uploads them); the cloud lanes write theirs to inbox/tracker-inbox/. Each spool
/// folder keeps its own merged/ and rejected/ beside it, so a file never leaves the folder it came
/// through. The master and the moved-out file are the two files only the merger writes.
/// </summary>
public static class MergePaths
{
    public const string Master = "Job_Hunt_Tracker.csv";
    public const string MovedOut = "Job_Hunt_Tracker_MOVED_OUT.csv";
    public const string Log = "tracker-inbox/merge.log";
    public const string MovedOutHeader = "Count,Person,Company,Communication,Date,Phone/Email,Notes";

    public static readonly IReadOnlyList<string> Spools = ["tracker-inbox", "inbox/tracker-inbox"];

    public static string Merged(string spool) => $"{spool}/merged";

    public static string Rejected(string spool) => $"{spool}/rejected";

    public static string In(string folder, string name) => $"{folder}/{name}";

    public static string Temp(string path) => $"{path}.merger-tmp";
}
