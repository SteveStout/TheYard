using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Application.Features.Archive;

/// <summary>
/// What the archive job may take and when it is old enough (ADR: Old folders are zipped, not kept): a nightly day
/// folder or a dispatcher done day more than fourteen Central days before today, the dispatcher's released and dead
/// lock files more than fourteen days after their stamp, one zip a month, the link-token rows already folded more than
/// thirty days after their last write, one zip a month, and mentor/logs once, by name. Fourteen days clears every
/// reader's look-back: the topic import reads three days, the last card seven, the morning cards today only.
/// </summary>
// #region archive-ages
public static partial class ArchiveUnits
{
    /// <summary>A day folder older than this many Central days is old enough.</summary>
    public const int DayAge = 14;

    /// <summary>A folded link-token file last written longer ago than this many days is old enough.</summary>
    public const int FileAge = 30;

    /// <summary>The most units of one kind a run archives.</summary>
    public const int PerKind = 5;

    /// <summary>The calls to the share after which a run starts no more units.</summary>
    public const int MostCalls = 3000;

    // #endregion archive-ages

    /// <summary>The nightly day folders' parent.</summary>
    public const string Nightly = "nightly";

    /// <summary>The dispatcher's closed runs, one folder a day.</summary>
    public const string Done = "dispatch/done";

    /// <summary>The dispatcher's lock files.</summary>
    public const string Locks = "dispatch/locks";

    /// <summary>The link-token rows the opens report has already folded into link-tokens.csv.</summary>
    public const string Merged = "inbox/link-tokens/merged";

    /// <summary>The October logs nothing writes any more, archived once by name.</summary>
    public const string Logs = "mentor/logs";

    /// <summary>The append-only log of every unit archived.</summary>
    public const string LogPath = WritePolicy.Archive + "ARCHIVE-LOG.tsv";

    /// <summary>The log's header line.</summary>
    public const string LogHeader = "utc\tunit\tfiles\tbytes\tzip_sha256\tzip\toriginal_moved_to\n";

    /// <summary>The only folders the job lists or takes from, one level, and inside a chosen unit only that unit.</summary>
    public static IReadOnlyList<string> Sources { get; } = [Nightly, Done, Locks, Merged, Logs];

    /// <summary>The day a day folder is named for, or null for any other name.</summary>
    /// <param name="name">The folder's name, without its parent.</param>
    public static DateOnly? Day(string name) =>
        DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>True when the day is more than <see cref="DayAge"/> Central days before today.</summary>
    /// <param name="day">The folder's day.</param>
    /// <param name="today">Today, Central.</param>
    public static bool OldDay(DateOnly day, DateOnly today) => today.DayNumber - day.DayNumber > DayAge;

    /// <summary>A day folder as a unit: a nightly day is kind nightly, a dispatcher done day is dispatch-done.</summary>
    /// <param name="parent">Its parent, <see cref="Nightly"/> or <see cref="Done"/>.</param>
    /// <param name="day">Its day.</param>
    public static ArchiveUnit DayUnit(string parent, DateOnly day)
    {
        string name = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new ArchiveUnit(parent == Nightly ? "nightly" : "dispatch-done", name, $"{parent}/{name}", []);
    }

    /// <summary>mentor/logs as one unit.</summary>
    public static ArchiveUnit LogsUnit { get; } = new("mentor", "logs", Logs, []);

    /// <summary>The instant a released or dead lock file was let go, from the seconds in its name; null for a live lock, the README or anything else.</summary>
    /// <param name="name">The file's name.</param>
    public static DateTimeOffset? LockStamp(string name) =>
        LockName().Match(name) is { Success: true } m && long.TryParse(m.Groups["at"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>The released and dead lock files more than <see cref="DayAge"/> days past their stamp, one unit a Central month, oldest first.</summary>
    /// <param name="names">The lock folder's file names.</param>
    /// <param name="now">The instant.</param>
    public static IReadOnlyList<ArchiveUnit> LockUnits(IEnumerable<string> names, DateTimeOffset now) =>
        Monthly("dispatch-locks", Locks, names.Select(name => (name, at: LockStamp(name))).Where(f => f.at is { } at && now - at > TimeSpan.FromDays(DayAge)).Select(f => (f.name, f.at!.Value)));

    /// <summary>The folded link-token files last written more than <see cref="FileAge"/> days ago, one unit a Central month, oldest first.</summary>
    /// <param name="files">The merged folder's files with their last writes.</param>
    /// <param name="now">The instant.</param>
    public static IReadOnlyList<ArchiveUnit> MergedUnits(IEnumerable<(string Name, DateTimeOffset Changed)> files, DateTimeOffset now) =>
        Monthly("link-tokens-merged", Merged, files.Where(f => now - f.Changed > TimeSpan.FromDays(FileAge)));

    /// <summary>Every day a live document names as nightly/&lt;day&gt; or nightly\&lt;day&gt;.</summary>
    /// <param name="text">The document's text.</param>
    public static IEnumerable<DateOnly> NamedDays(string text) =>
        NightlyPath().Matches(text).Select(m => Day(m.Groups["day"].Value)).OfType<DateOnly>();

    /// <summary>True for a nightly document the job reads for named days: a .md at nightly's top that is not a backup.</summary>
    /// <param name="name">The file's name.</param>
    public static bool LiveDocument(string name) => name.EndsWith(".md", StringComparison.Ordinal) && !name.StartsWith("_backup", StringComparison.Ordinal);

    /// <summary>Where the unit's zip goes.</summary>
    /// <param name="unit">The unit.</param>
    public static string ZipPath(ArchiveUnit unit) => $"{WritePolicy.Archive}{unit.Kind}/{unit.Name}.zip";

    /// <summary>The folder the unit's originals move into.</summary>
    /// <param name="unit">The unit.</param>
    public static string Target(ArchiveUnit unit) => $"{WritePolicy.ToDelete}{unit.Kind}";

    /// <summary>One ARCHIVE-LOG.tsv line.</summary>
    /// <param name="at">When.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="files">How many files the zip holds.</param>
    /// <param name="zip">The zip as written.</param>
    /// <param name="moved">Where the originals went.</param>
    public static string LogLine(DateTimeOffset at, ArchiveUnit unit, int files, ArchiveZip zip, string moved) =>
        $"{at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\t{unit.Label}\t{files}\t{zip.Bytes}\t{zip.Sha256}\t{zip.Zip}\t{moved}\n";

    /// <summary>The note left where mentor/logs was.</summary>
    /// <param name="at">When it moved.</param>
    /// <param name="zip">The zip.</param>
    /// <param name="moved">Where the originals went.</param>
    /// <param name="files">How many files moved.</param>
    public static string MovedNote(DateTimeOffset at, ArchiveZip zip, string moved, int files) =>
        $"""
        > RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).

        # mentor\logs moved

        On {RunSlot.Local(at).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} Central the archive job (JobHunt.Server archive, lane datamove1123) zipped the {files} files that were here and moved the folder.

        - The zip, every file at its old path inside it: J:\{zip.Zip.Replace('/', '\\')} ({zip.Bytes} bytes, SHA-256 {zip.Sha256}).
        - The originals, unchanged, until Steve empties that folder: J:\{moved.Replace('/', '\\')}
        - The line for it: J:\{LogPath.Replace('/', '\\')}

        Decision records that cite a file under mentor\logs by path find it under either of those two places at the same path below mentor\logs.
        Nothing writes here any more; a file that appears beside this note is named by the job's daily line.

        """;

    private static IReadOnlyList<ArchiveUnit> Monthly(string kind, string source, IEnumerable<(string Name, DateTimeOffset At)> files) =>
        [.. files
            .GroupBy(f => RunSlot.Local(f.At).ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .OrderBy(month => month.Key, StringComparer.Ordinal)
            .Select(month => new ArchiveUnit(kind, month.Key, source, [.. month.Select(f => f.Name).Order(StringComparer.Ordinal)]))];

    [GeneratedRegex(@"^[\w.-]+?\.(released|dead)-(?<at>\d{9,11})$")]
    private static partial Regex LockName();

    [GeneratedRegex(@"nightly[\\/](?<day>\d{4}-\d{2}-\d{2})")]
    private static partial Regex NightlyPath();
}
