using TestProject.Data;

namespace TestProject.Domain;

/// <summary>
/// The counts and bytes for a view, and the order entries are shown in. Both are
/// computed once on the server and sent on the wire, so the page never adds up a
/// column it might have sorted, filtered or half-loaded (ADR-004).
/// </summary>
public static class ViewTotals
{
    /// <summary>Counts the entries and adds up the files' bytes.</summary>
    /// <param name="folders">The folders in the view.</param>
    /// <param name="files">The files in the view.</param>
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
    /// The order a listing is shown in: by name, ignoring case, so "Reports" and
    /// "archive" do not split into two alphabets the way an ordinal sort would.
    /// </summary>
    public static readonly StringComparer NameOrder = StringComparer.OrdinalIgnoreCase;
}
