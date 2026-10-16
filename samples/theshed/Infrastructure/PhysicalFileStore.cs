using TestProject.Application;
using TestProject.Data;

namespace TestProject.Infrastructure;

/// <summary>
/// The implementation of <see cref="IFileStore"/> that reads and writes real files and
/// folders on disk. Each method is a thin call into System.IO; the rules about which
/// paths and operations are allowed live in <see cref="FileBrowser"/> and are already
/// applied before a call reaches this class. The one design choice made here is how a
/// folder tree is walked: entries are returned one at a time as they are found, and
/// anything that cannot be read is skipped. That lets a search stop as soon as it has
/// enough matches, and lets a folder with one locked subfolder still be listed.
/// A container's disk is wiped on a restart or a redeploy, so uploads here do not last; a
/// cloud store is one more IFileStore and one line in Composition.
/// </summary>
public sealed class PhysicalFileStore : IFileStore
{
    // #region walk
    /// <summary>
    /// Options for listing one folder's direct contents. This set and the deep set below
    /// both skip entries that cannot be read, and both leave out hidden and system
    /// entries, because a person browsing a folder is not looking for files like
    /// desktop.ini and a search should not use up its result limit on them.
    /// </summary>
    private static readonly EnumerationOptions Shallow = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Options for walking every folder under a starting folder, at any depth, with the
    /// same skipping rules as the single-folder options above.
    /// </summary>
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
        // EnumerateFileSystemInfos walks the folder in one pass. On Windows the listing
        // already carries each entry's size and time; on Linux each entry costs one
        // lookup, still taken inside this single pass. The yield return hands entries
        // back one at a time, so a caller that stops early stops the walk too.
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
        new FileStream(absoluteFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

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

    /// <summary>
    /// One listed entry as the store reports it. A file that vanished between the listing and
    /// this read (another visitor's delete) counts as empty rather than failing the whole listing;
    /// the next read no longer lists it.
    /// </summary>
    /// <param name="info">The entry as the directory listing handed it back.</param>
    private static StoreEntry From(FileSystemInfo info)
    {
        bool isFolder = (info.Attributes & FileAttributes.Directory) != 0;
        long size;
        try
        {
            size = isFolder ? 0 : ((FileInfo)info).Length;
        }
        catch (FileNotFoundException)
        {
            size = 0;
        }
        return new StoreEntry(info.FullName, isFolder ? EntryKind.Folder : EntryKind.File, size, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
    }
}
