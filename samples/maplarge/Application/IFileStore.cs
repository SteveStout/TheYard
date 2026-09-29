using TestProject.Data;

namespace TestProject.Application;

/// <summary>
/// The port the use cases talk to (ADR-002). Everything here is in absolute
/// paths that <see cref="Domain.HomePath"/> has already accepted; the store
/// trusts them. The one real implementation is the disk; the tests supply an
/// in-memory one, which is why the use cases can be tested without a temp
/// folder.
/// </summary>
public interface IFileStore
{
    /// <summary>What is at a path: a file, a folder, or nothing.</summary>
    /// <param name="absolute">An absolute path.</param>
    EntryKind KindOf(string absolute);

    /// <summary>One entry's facts. Throws when nothing is there.</summary>
    /// <param name="absolute">An absolute path to something that exists.</param>
    StoreEntry Describe(string absolute);

    /// <summary>The entries directly inside a folder, in no particular order.</summary>
    /// <param name="absoluteFolder">An absolute path to a folder.</param>
    IEnumerable<StoreEntry> Children(string absoluteFolder);

    /// <summary>
    /// Every entry under a folder, any depth, streamed as the walk finds them so a
    /// caller can stop early. Folders it cannot read are skipped, not thrown.
    /// </summary>
    /// <param name="absoluteFolder">An absolute path to a folder.</param>
    IEnumerable<StoreEntry> Descendants(string absoluteFolder);

    /// <summary>Opens a file for reading, streamed.</summary>
    /// <param name="absoluteFile">An absolute path to a file.</param>
    Stream OpenRead(string absoluteFile);

    /// <summary>Creates a file for writing, streamed. Fails when it exists unless told to overwrite.</summary>
    /// <param name="absoluteFile">Where the file goes.</param>
    /// <param name="overwrite">True to replace an existing file.</param>
    Stream Create(string absoluteFile, bool overwrite);

    /// <summary>Creates a folder. The parent must exist.</summary>
    /// <param name="absoluteFolder">Where the folder goes.</param>
    void CreateFolder(string absoluteFolder);

    /// <summary>Deletes a file, or a folder and everything in it.</summary>
    /// <param name="absolute">What to delete.</param>
    void Delete(string absolute);

    /// <summary>Moves a file or folder. The destination must not exist.</summary>
    /// <param name="source">What to move.</param>
    /// <param name="destination">Where it goes, new name included.</param>
    void Move(string source, string destination);

    /// <summary>Copies a file, or a folder and everything in it. The destination must not exist.</summary>
    /// <param name="source">What to copy.</param>
    /// <param name="destination">Where the copy goes, new name included.</param>
    void Copy(string source, string destination);
}
