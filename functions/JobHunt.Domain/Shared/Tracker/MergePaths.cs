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

    /// <summary>
    /// What the tracker's SQL copy last saw of the master, one line the merger keeps so a new instance can leave the copy
    /// alone without asking SQL (ADR-111, the addendum on the quiet pass). Not a spool file: neither .csv nor a moveout list.
    /// </summary>
    public const string CopySeen = "tracker-inbox/tracker-sql.seen";
    public const string MovedOutHeader = "Count,Person,Company,Communication,Date,Phone/Email,Notes";

    public static readonly IReadOnlyList<string> Spools = ["tracker-inbox", "inbox/tracker-inbox"];

    public static string Merged(string spool) => $"{spool}/merged";

    public static string Rejected(string spool) => $"{spool}/rejected";

    /// <summary>
    /// Where a file with no tracker row in it goes (header lines only, or rows with no Person, Company, Communication or
    /// Date): read and logged, with its row in the folder's README, so it never waits on the card's rejected count.
    /// </summary>
    public static string RejectedLogged(string spool) => $"{spool}/rejected-logged";

    public static string In(string folder, string name) => $"{folder}/{name}";

    public static string Temp(string path) => $"{path}.merger-tmp";
}
