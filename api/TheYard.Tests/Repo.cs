namespace TheYard.Tests;

/// <summary>
/// Where the repository is, and how to walk it, from inside a test binary. The
/// suite runs from `api/TheYard.Tests/bin/...`, so the root is found by walking
/// up to the directory that has both a README and a data folder rather than by
/// counting how many `..` that happens to be today.
///
/// <para>Several tests read the repository itself rather than the application:
/// the em dash rule, the record links, the Dockerfile's build inputs. They share
/// the walk from here so that the list of directories worth skipping is written
/// once.</para>
/// </summary>
internal static class Repo
{
    public static string DataFile(string name) => Path.Combine(Root(), "data", name);

    // #region walking the repository
    /// <summary>
    /// Directories that hold somebody else's text or this build's output.
    /// </summary>
    private static readonly string[] NotOurs =
    [
        "node_modules", "bin", "obj", ".git", "dist", "playwright-report",
        "test-results", "TestResults", "coverage", ".vs", ".idea",
    ];

    /// <summary>
    /// Every file in the repository with one of these extensions, with the
    /// vendored and generated trees skipped during the walk rather than
    /// filtered out afterwards: descending into node_modules to throw the
    /// result away costs more than everything that calls this.
    /// </summary>
    public static List<string> FilesWith(params string[] extensions)
    {
        var found = new List<string>();
        Walk(Root(), extensions, found);
        return found;
    }

    private static void Walk(string directory, string[] extensions, List<string> found)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                found.Add(file);
            }
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (!NotOurs.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                Walk(child, extensions, found);
            }
        }
    }
    // #endregion walking the repository

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "data")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"no repository root above {AppContext.BaseDirectory}: looked for a README.md beside a data directory");
    }
}
