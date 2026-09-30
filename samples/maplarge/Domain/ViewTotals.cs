using TestProject.Data;

namespace TestProject.Domain;

/// <summary>
/// Computes the totals for a listing or search result, and defines the order entries
/// are sorted in. Both are done once on the server and sent with the reply, so the page
/// never has to add up sizes itself from rows it may have sorted, filtered or only
/// partly loaded.
/// </summary>
public static class ViewTotals
{
    /// <summary>Counts the folders and files and adds up the files' sizes.</summary>
    /// <param name="folders">The folders in the result.</param>
    /// <param name="files">The files in the result.</param>
    public static Totals Of(IReadOnlyList<FolderEntry> folders, IReadOnlyList<FileEntry> files)
    {
        long bytes = 0;
        foreach (FileEntry file in files)
        {
            bytes += file.SizeBytes;
        }
        return new Totals(folders.Count, files.Count, bytes);
    }

    /// <summary>
    /// The sort order for names: alphabetical, ignoring case. An ordinal sort would put
    /// every capitalised name before every lower-case one, so "Reports" would come
    /// before "archive"; ignoring case keeps them in one alphabet.
    /// </summary>
    public static readonly StringComparer NameOrder = StringComparer.OrdinalIgnoreCase;
}
