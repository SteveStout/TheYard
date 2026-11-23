namespace JobHunt.Server.Application.Shared.Ports;

/// <summary>
/// One thing the archive job zips and moves as a whole: a folder, or a set of files in one folder.
/// </summary>
/// <param name="Kind">The kind, which names its folder under the archive and under the originals' folder.</param>
/// <param name="Name">The zip's name, a day, a month or a folder's name.</param>
/// <param name="Source">The folder, relative to the share's root.</param>
/// <param name="Files">For a file unit, the file names in <paramref name="Source"/>; empty for a folder unit.</param>
public sealed record ArchiveUnit(string Kind, string Name, string Source, IReadOnlyList<string> Files)
{
    /// <summary>True when the unit is a set of files rather than a whole folder.</summary>
    public bool IsFiles => Files.Count > 0;

    /// <summary>The unit as the job's lines name it.</summary>
    public string Label => IsFiles ? $"{Source}/{Kind}:{Name} ({Files.Count} files)" : Source;
}

/// <summary>A file the zip holds or will hold, by its path relative to the share's root.</summary>
/// <param name="Path">The path relative to the share's root, which is also the zip entry's name.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Crc">Its CRC-32.</param>
public sealed record ArchiveEntry(string Path, long Length, uint Crc);

/// <summary>A zip once written.</summary>
/// <param name="Zip">Where it is, relative to the share's root.</param>
/// <param name="Bytes">Its length.</param>
/// <param name="Sha256">Its SHA-256, lower-case hex.</param>
public sealed record ArchiveZip(string Zip, long Bytes, string Sha256);

/// <summary>
/// Zips, verifies and moves archive units on the share. Nothing here deletes: a unit's originals move by one rename,
/// and a zip that does not verify is renamed aside. Every call it makes to the share is counted.
/// </summary>
public interface IArchiver
{
    /// <summary>The calls this archiver has made to the share so far.</summary>
    int Calls { get; }

    /// <summary>Every file of the unit, at any depth inside a folder unit, with its length and CRC-32 as read now.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="ct">Stops the read.</param>
    Task<IReadOnlyList<ArchiveEntry>> ReadAsync(ArchiveUnit unit, CancellationToken ct);

    /// <summary>How many files the unit holds, by name alone.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="ct">Stops the read.</param>
    Task<int> CountAsync(ArchiveUnit unit, CancellationToken ct);

    /// <summary>Writes the unit's files into a new zip at the first free name of <paramref name="zip"/>, -2, -3 and on.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="zip">The zip's path, relative to the share's root.</param>
    /// <param name="ct">Stops the write.</param>
    Task<ArchiveZip> ZipAsync(ArchiveUnit unit, string zip, CancellationToken ct);

    /// <summary>The zip reopened from disk, each entry read through to its CRC-32.</summary>
    /// <param name="zip">The zip's path, relative to the share's root.</param>
    /// <param name="ct">Stops the read.</param>
    Task<IReadOnlyList<ArchiveEntry>> OpenAsync(string zip, CancellationToken ct);

    /// <summary>Moves the unit's originals under <paramref name="target"/> by one rename (one a file for a file unit), and answers where they went.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="target">The folder the originals go in, relative to the share's root.</param>
    /// <param name="ct">Stops the move.</param>
    Task<string> MoveAsync(ArchiveUnit unit, string target, CancellationToken ct);

    /// <summary>Renames a zip that did not verify to its name with .failed, and answers the new name.</summary>
    /// <param name="zip">The zip's path, relative to the share's root.</param>
    /// <param name="ct">Stops the rename.</param>
    Task<string> FailAsync(string zip, CancellationToken ct);
}
