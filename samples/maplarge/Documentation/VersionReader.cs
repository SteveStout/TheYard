using System.Text.RegularExpressions;
using TestProject.Data;

namespace TestProject.Documentation;

/// <summary>
/// Reads the version and commit shown in the page footer, once at startup. The version comes
/// from the newest entry in docs/CHANGELOG.md and the commit comes from the .git folder. The
/// version number is written nowhere else, so releasing a version and adding its changelog entry
/// are the same step, and the footer can never show a version the changelog does not list.
/// </summary>
public static partial class VersionReader
{
    /// <summary>
    /// The value used when the version or commit cannot be read, so the footer says so instead
    /// of guessing.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>Reads the version from the changelog and the commit for a project root.</summary>
    /// <param name="projectRoot">The folder holding docs/ and, when run from a clone, .git.</param>
    public static VersionInfo Read(string projectRoot) => new(VersionFrom(Path.Combine(projectRoot, "docs", "CHANGELOG.md")), CommitFrom(projectRoot));

    // #region version
    /// <summary>
    /// Returns the first version number with four dot-separated parts on a line that starts
    /// with "- ". The changelog lists entries newest first, so the first match is the current
    /// version. Returns "unknown" when the file or a matching line is missing.
    /// </summary>
    /// <param name="changelog">The path to the changelog file.</param>
    public static string VersionFrom(string changelog)
    {
        if (!File.Exists(changelog))
        {
            return Unknown;
        }
        foreach (string line in File.ReadLines(changelog))
        {
            var match = FourNumbers().Match(line);
            if (line.StartsWith("- ", StringComparison.Ordinal) && match.Success)
            {
                return match.Value;
            }
        }
        return Unknown;
    }
    // #endregion version

    /// <summary>
    /// Returns the short (seven character) hash of the current commit. The SHED_COMMIT
    /// environment variable wins when set, because a container image has no .git folder and
    /// receives the hash that way. Otherwise it searches upward from the project root for a
    /// .git folder and reads it directly, so git does not need to be installed: HEAD names a
    /// branch ref, and that ref's file (or the packed-refs file) holds the commit hash. A copy
    /// of the project with no .git folder, such as a zip download, gets "unknown".
    /// </summary>
    /// <param name="projectRoot">The folder that holds .git, or any folder below it.</param>
    public static string CommitFrom(string projectRoot)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("SHED_COMMIT");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }
        for (var folder = new DirectoryInfo(projectRoot); folder is not null; folder = folder.Parent)
        {
            string git = Path.Combine(folder.FullName, ".git");
            if (Directory.Exists(git))
            {
                return Short(HeadOf(git));
            }
        }
        return Unknown;
    }

    private static string? HeadOf(string git)
    {
        string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
        {
            return head;
        }
        string reference = head[5..];
        string refFile = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(refFile))
        {
            return File.ReadAllText(refFile).Trim();
        }
        string packed = Path.Combine(git, "packed-refs");
        if (File.Exists(packed))
        {
            foreach (string line in File.ReadLines(packed))
            {
                if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                {
                    return line.Split(' ')[0];
                }
            }
        }
        return null;
    }

    /// <summary>The first seven characters of a commit hash, the length GitHub shows, or "unknown" when there is none.</summary>
    private static string Short(string? hash) => hash is { Length: >= 7 } ? hash[..7] : Unknown;

    /// <summary>A four-number version such as 1.0.0.28, the shape of every changelog line.</summary>
    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex FourNumbers();
}
