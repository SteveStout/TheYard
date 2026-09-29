using System.Text.RegularExpressions;
using TestProject.Data;

namespace TestProject.Docs;

/// <summary>
/// The version the footer shows comes from the top line of docs/CHANGELOG.md,
/// and the commit from .git, read once at startup (ADR-012). There is no
/// version number typed anywhere else, so shipping a version and writing its
/// changelog line are one act, and a footer can never claim a version the log
/// does not know about.
/// </summary>
public static partial class VersionReader
{
    /// <summary>What to show when a fact cannot be read: the footer says so rather than guessing.</summary>
    public const string Unknown = "unknown";

    /// <summary>Reads the version and the commit for a project root.</summary>
    /// <param name="projectRoot">The folder holding docs/ and, when run from a clone, .git.</param>
    public static VersionInfo Read(string projectRoot) => new(VersionFrom(Path.Combine(projectRoot, "docs", "CHANGELOG.md")), CommitFrom(projectRoot));

    // #region version
    /// <summary>The first four-number version in the changelog, which is the newest because the log is newest first.</summary>
    /// <param name="changelog">The changelog's path.</param>
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
    /// The short hash of HEAD, read from the .git folder without git installed:
    /// HEAD names a ref, and the ref file (or packed-refs) names the commit. A
    /// build with no .git, such as the zip they asked for, says "unknown", and a
    /// container carries the hash in the SHED_COMMIT variable instead.
    /// </summary>
    /// <param name="projectRoot">The folder that may hold .git, or a parent of it.</param>
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

    private static string Short(string? hash) => hash is { Length: >= 7 } ? hash[..7] : Unknown;

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex FourNumbers();
}
