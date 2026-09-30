using TestProject.Data;

namespace TestProject.Application;

/// <summary>
/// The interface <see cref="FileBrowser"/> uses for every read and write of files and
/// folders. Every path passed in is absolute and has already been checked by
/// <see cref="Domain.HomePath"/>, so an implementation does not check paths again.
/// The app's implementation reads and writes the real disk; the tests supply an
/// in-memory one, which is why the file operations can be tested without a temporary
/// folder.
/// </summary>
public interface IFileStore
{
    /// <summary>Reports what is at a path: a file, a folder, or nothing.</summary>
    /// <param name="absolute">An absolute path.</param>
    EntryKind KindOf(string absolute);

    /// <summary>
    /// Returns one entry's kind, size and last change time. Throws if nothing is there.
    /// </summary>
    /// <param name="absolute">An absolute path to something that exists.</param>
    StoreEntry Describe(string absolute);

    /// <summary>Returns the entries directly inside a folder, in no particular order.</summary>
    /// <param name="absoluteFolder">An absolute path to a folder.</param>
    IEnumerable<StoreEntry> Children(string absoluteFolder);

    /// <summary>
    /// Returns every entry under a folder, at any depth. Entries are handed back one at
    /// a time as the walk finds them, so a caller that has enough can stop and the rest
    /// of the tree is never read. Folders that cannot be read are skipped instead of
    /// throwing, so one locked folder does not fail the whole walk.
    /// </summary>
    /// <param name="absoluteFolder">An absolute path to a folder.</param>
    IEnumerable<StoreEntry> Descendants(string absoluteFolder);

    /// <summary>
    /// Opens a file for reading as a stream, so it is never loaded whole into memory.
    /// </summary>
    /// <param name="absoluteFile">An absolute path to a file.</param>
    Stream OpenRead(string absoluteFile);

    /// <summary>
    /// Creates a file and returns a stream for writing into it. Fails when the file
    /// already exists, unless <paramref name="overwrite"/> is true.
    /// </summary>
    /// <param name="absoluteFile">The absolute path where the file is created.</param>
    /// <param name="overwrite">True to replace an existing file.</param>
    Stream Create(string absoluteFile, bool overwrite);

    /// <summary>Creates a folder. The folder it goes in must already exist.</summary>
    /// <param name="absoluteFolder">The absolute path where the folder is created.</param>
    void CreateFolder(string absoluteFolder);

    /// <summary>Deletes a file, or a folder with everything inside it.</summary>
    /// <param name="absolute">The absolute path of what to delete.</param>
    void Delete(string absolute);

    /// <summary>Moves a file or folder. Nothing may already exist at the destination.</summary>
    /// <param name="source">The absolute path of what to move.</param>
    /// <param name="destination">The absolute path it moves to, new name included.</param>
    void Move(string source, string destination);

    /// <summary>
    /// Copies a file, or a folder with everything inside it. Nothing may already exist
    /// at the destination.
    /// </summary>
    /// <param name="source">The absolute path of what to copy.</param>
    /// <param name="destination">The absolute path of the copy, new name included.</param>
    void Copy(string source, string destination);
}
