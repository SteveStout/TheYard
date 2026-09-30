namespace TestProject.Tests;

/// <summary>
/// Finds the project folder from inside a running test. It walks up from the test's
/// output folder until it reaches the directory that holds TestProject.csproj. Several
/// tests read the project's own files (the docs, the source, the stylesheets), so they
/// share this search and the list of build and tool folders to skip.
/// </summary>
internal static class ProjectFolder
{
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", ".vs", "node_modules", "TestResults"];

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TestProject.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException($"no TestProject.csproj above {AppContext.BaseDirectory}");
    }

    /// <summary>
    /// Every file under the project with one of these extensions. It skips build output, git and
    /// tool folders, because those hold generated files the project's rules do not apply to.
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
            if (!SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                Walk(child, extensions, found);
            }
        }
    }

    /// <summary>
    /// A path relative to the project root with forward slashes, so failure messages read the same
    /// on every OS.
    /// </summary>
    public static string Relative(string file) => Path.GetRelativePath(Root(), file).Replace('\\', '/');
}
