namespace TestProject.Tests;

/// <summary>
/// Where the project is, from inside a test binary: walk up from the test's
/// output folder to the directory holding TestProject.csproj. Several tests read
/// the project itself (the records, the em dash rule, the stylesheet), and they
/// share the walk and the list of folders that are not ours.
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

    /// <summary>Every file under the project with one of these extensions, skipping build output.</summary>
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

    /// <summary>A path relative to the project root, forward slashes, for a message.</summary>
    public static string Relative(string file) => Path.GetRelativePath(Root(), file).Replace('\\', '/');
}

/// <summary>A temporary home directory for one test, deleted when the test ends.</summary>
internal sealed class TempHome : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "shed-tests", Guid.NewGuid().ToString("N"));

    public TempHome()
    {
        Directory.CreateDirectory(Root);
    }

    /// <summary>Writes a file under the home, creating folders on the way; the path uses forward slashes.</summary>
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
            // A test may have made a file read-only on purpose; clear that first or the delete refuses.
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A handle the operating system has not released yet; the folder is under temp and harmless.
        }
        GC.SuppressFinalize(this);
    }
}
