using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Application;

/// <summary>
/// The byte quota on the home: what the home holds now, and whether a write of a given size still
/// fits. Measured by walking the home through the file store each time, because the tree is small
/// and a running total kept in memory would be wrong after anything that changed the disk from
/// outside this process.
/// </summary>
public sealed class HomeQuota(HomePath home, IFileStore store, FilesOptions options)
{
    /// <summary>The bytes every file under the home adds up to.</summary>
    public long Used() => SizeOf(home.Root);

    /// <summary>The bytes a file holds, or every file under a folder at any depth.</summary>
    /// <param name="absolute">An absolute path the caller has already checked.</param>
    public long SizeOf(string absolute) =>
        store.KindOf(absolute) == EntryKind.Folder
            ? store.Descendants(absolute).Where(entry => entry.Kind == EntryKind.File).Sum(entry => entry.SizeBytes)
            : store.Describe(absolute).SizeBytes;

    /// <summary>
    /// Refuses with 507 when adding <paramref name="incoming"/> bytes would take the home past the
    /// quota. A quota of 0 means no check.
    /// </summary>
    /// <param name="incoming">The bytes the write would add.</param>
    public void Allow(long incoming)
    {
        if (options.HomeQuotaBytes <= 0)
        {
            return;
        }
        long used = Used();
        if (used + incoming > options.HomeQuotaBytes)
        {
            throw ApiRefusalException.Full(
                $"The home holds {used:N0} bytes and this adds {incoming:N0}; the quota is {options.HomeQuotaBytes:N0}.");
        }
    }
}
