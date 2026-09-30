namespace TestProject.Data;

// What the file store reports about one path. These records stay inside the server: the
// API turns them into the relative-path replies in Data/ApiResponses.cs before anything is
// sent. They hold values only, so every other folder can use them.

/// <summary>What a path points at in the file store.</summary>
public enum EntryKind
{
    /// <summary>Nothing exists at the path.</summary>
    None,
    /// <summary>The path is a file.</summary>
    File,
    /// <summary>The path is a folder.</summary>
    Folder,
}

/// <summary>
/// One file or folder as the file store reports it: its absolute path on the machine and
/// its basic facts. It stays inside the server; the API sends the records in ApiResponses.cs.
/// </summary>
/// <param name="Absolute">The full absolute path on the machine.</param>
/// <param name="Kind">Whether it is a file or a folder.</param>
/// <param name="SizeBytes">The size in bytes; zero for a folder.</param>
/// <param name="ModifiedMs">When it last changed, in milliseconds since 1 January 1970 UTC.</param>
public sealed record StoreEntry(string Absolute, EntryKind Kind, long SizeBytes, long ModifiedMs);
