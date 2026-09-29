using TestProject.Application;
using TestProject.Data;

namespace TestProject.Infrastructure;

/// <summary>
/// The disk, behind the port (ADR-002). Every method is a thin call into
/// System.IO; the rules live one layer in. The one decision here is how a tree
/// is walked: lazily, skipping what cannot be read, so a search can stop early
/// and a folder with one locked subfolder still lists (ADR-008).
/// </summary>
public sealed class PhysicalFileStore : IFileStore
{
    // #region walk
    /// <summary>
    /// Hidden and system entries stay out of every listing: a person browsing a
    /// home directory is not looking for desktop.ini, and a search across a tree
    /// should not spend its limit on them.
    /// </summary>
    private static readonly EnumerationOptions Shallow = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private static readonly EnumerationOptions Deep = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <inheritdoc />
    public IEnumerable<StoreEntry> Children(string absoluteFolder) => Walk(absoluteFolder, Shallow);

    /// <inheritdoc />
    public IEnumerable<StoreEntry> Descendants(string absoluteFolder) => Walk(absoluteFolder, Deep);

    private static IEnumerable<StoreEntry> Walk(string absoluteFolder, EnumerationOptions options)
    {
        // EnumerateFileSystemInfos hands back each entry with its attributes and
        // size already read from the directory listing, so a folder of ten
        // thousand files costs one enumeration, not ten thousand stat calls.
        foreach (FileSystemInfo info in new DirectoryInfo(absoluteFolder).EnumerateFileSystemInfos("*", options))
        {
            yield return From(info);
        }
    }
    // #endregion walk

    /// <inheritdoc />
    public EntryKind KindOf(string absolute)
    {
        if (Directory.Exists(absolute))
        {
            return EntryKind.Folder;
        }
        return File.Exists(absolute) ? EntryKind.File : EntryKind.None;
    }

    /// <inheritdoc />
    public StoreEntry Describe(string absolute)
    {
        if (Directory.Exists(absolute))
        {
            return From(new DirectoryInfo(absolute));
        }
        return From(new FileInfo(absolute));
    }

    /// <inheritdoc />
    public Stream OpenRead(string absoluteFile) =>
        new FileStream(absoluteFile, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <inheritdoc />
    public Stream Create(string absoluteFile, bool overwrite) =>
        new FileStream(absoluteFile, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);

    /// <inheritdoc />
    public void CreateFolder(string absoluteFolder) => Directory.CreateDirectory(absoluteFolder);

    /// <inheritdoc />
    public void Delete(string absolute)
    {
        if (Directory.Exists(absolute))
        {
            Directory.Delete(absolute, recursive: true);
        }
        else
        {
            File.Delete(absolute);
        }
    }

    /// <inheritdoc />
    public void Move(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    /// <inheritdoc />
    public void Copy(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            CopyTree(new DirectoryInfo(source), destination);
        }
        else
        {
            File.Copy(source, destination);
        }
    }

    private static void CopyTree(DirectoryInfo source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (FileInfo file in source.EnumerateFiles())
        {
            file.CopyTo(Path.Combine(target, file.Name));
        }
        foreach (DirectoryInfo folder in source.EnumerateDirectories())
        {
            CopyTree(folder, Path.Combine(target, folder.Name));
        }
    }

    private static StoreEntry From(FileSystemInfo info)
    {
        bool isFolder = (info.Attributes & FileAttributes.Directory) != 0;
        long size = isFolder ? 0 : ((FileInfo)info).Length;
        return new StoreEntry(info.FullName, isFolder ? EntryKind.Folder : EntryKind.File, size, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
    }
}
