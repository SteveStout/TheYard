namespace TestProject.Tests;

/// <summary>
/// Finds the project folder from inside a running test. It walks up from the test's
/// output folder until it reaches the directory that holds TestProject.csproj. Several
/// tests read the project's own files (the docs, the source, the stylesheets), so they
/// share this search and the list of build and tool folders to skip.
/// </summary>
internal static class Repo
{
    private static readonly string[] NotOurs = ["bin", "obj", ".git", ".vs", "node_modules", "TestResults"];

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
            if (!NotOurs.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
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

/// <summary>
/// A new, empty folder under the system temp directory for one test, deleted when the test is
/// disposed. Each test gets its own folder, so tests never share files or touch real ones.
/// </summary>
internal sealed class TempHome : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "shed-tests", Guid.NewGuid().ToString("N"));

    public TempHome()
    {
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Writes a file under the temp folder, creating any missing folders on the way, and returns
    /// its full path. The relative path uses forward slashes, so a test reads the same on every OS.
    /// </summary>
    public string File(string relative, string content = "")
    {
        string absolute = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        System.IO.File.WriteAllText(absolute, content);
        return absolute;
    }

    public string Folder(string relative)
    {
        string absolute = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absolute);
        return absolute;
    }

    public void Dispose()
    {
        try
        {
            // A test may make a file read-only on purpose. Clear that attribute first,
            // or the folder delete fails on that file.
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The OS may not have released a file handle yet. The folder is under temp, so leaving
            // it behind does no harm, and a cleanup error must not fail a test that passed.
        }
        GC.SuppressFinalize(this);
    }
}
