namespace TestProject.Tests;

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
