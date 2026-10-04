using System.Text.RegularExpressions;
using TestProject.Data;

namespace TestProject.Documentation;

/// <summary>
/// Lists and finds the markdown documents the app serves about itself. They are read from the
/// <c>docs</c> folder beside the project on every request, not copied or cached, so the served
/// text always matches the files in this build. This class also fixes the sidebar order: the
/// start page and the README first, then the decision records by number, then the guides.
/// </summary>
public sealed partial class DocumentationCatalog(string contentRoot)
{
    /// <summary>The sidebar group name for the decision records.</summary>
    public const string RecordsGroup = "Decision records";

    /// <summary>The sidebar group name for the guides.</summary>
    public const string GuidesGroup = "Guides";

    private static readonly string[] Guides = ["STYLE", "BUILT-WITH-AI", "CHANGELOG"];

    /// <summary>The docs folder the documents are read from.</summary>
    public string DocumentsFolder { get; } = Path.Combine(contentRoot, "docs");

    // #region catalogue
    /// <summary>
    /// Returns every document in sidebar order. Each title is the first "# " heading in its file,
    /// read fresh on each call.
    /// </summary>
    public IReadOnlyList<DocumentEntry> List()
    {
        var entries = new List<DocumentEntry>
        {
            new("start-here", TitleOf(Path.Combine(DocumentsFolder, "START-HERE.md")), "Start here"),
            new("readme", TitleOf(Path.Combine(contentRoot, "README.md")), "Start here"),
        };
        foreach (string file in Directory.EnumerateFiles(DocumentsFolder, "ADR-*.md").OrderBy(RecordNumber))
        {
            entries.Add(new DocumentEntry(SlugOf(file), TitleOf(file), RecordsGroup));
        }
        foreach (string guide in Guides)
        {
            entries.Add(new DocumentEntry(guide.ToLowerInvariant(), TitleOf(Path.Combine(DocumentsFolder, guide + ".md")), GuidesGroup));
        }
        return entries;
    }

    /// <summary>
    /// Returns the file path for a slug, or null when no listed document has that slug. The README
    /// sits at the project root, so it is handled first. Any other slug must appear in
    /// <see cref="List"/>, so only documents the sidebar shows can be served.
    /// </summary>
    /// <param name="slug">The slug from the request: a lower-case file name without .md.</param>
    public string? FileFor(string slug)
    {
        if (slug == "readme")
        {
            return Path.Combine(contentRoot, "README.md");
        }
        return List().Any(entry => entry.Slug == slug)
            ? Directory.EnumerateFiles(DocumentsFolder, "*.md").FirstOrDefault(file => SlugOf(file) == slug)
            : null;
    }

    /// <summary>
    /// Returns one document as markdown with each live code block filled in from this build, or
    /// null when no listed document has that slug.
    /// </summary>
    /// <param name="slug">The slug from the request: a lower-case file name without .md.</param>
    public string? Read(string slug) =>
        FileFor(slug) is { } file ? LiveSamples.Expand(File.ReadAllText(file), contentRoot) : null;
    // #endregion catalogue

    /// <summary>
    /// Reads the number from a decision record's file name so records sort by number, putting
    /// ADR-010 after ADR-009 rather than after ADR-001 as a text sort would. A name without a
    /// number sorts last.
    /// </summary>
    /// <param name="file">A path ending in ADR-NNN-anything.md.</param>
    public static int RecordNumber(string file)
    {
        var match = Number().Match(Path.GetFileName(file));
        return match.Success ? int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
    }

    /// <summary>The address of a document: its file name in lower case without .md, so a slug never changes unless the file is renamed.</summary>
    private static string SlugOf(string file) => Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

    /// <summary>The title the sidebar shows: the file's first "# " heading without the "ADR: " prefix, or the file name when it has none.</summary>
    private static string TitleOf(string file)
    {
        if (!File.Exists(file))
        {
            return Path.GetFileNameWithoutExtension(file);
        }
        foreach (string line in File.ReadLines(file))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Replace("ADR: ", string.Empty, StringComparison.Ordinal).Trim();
            }
        }
        return Path.GetFileNameWithoutExtension(file);
    }

    /// <summary>The record number at the start of a file name such as ADR-012-documents.md.</summary>
    [GeneratedRegex(@"^ADR-(?<n>\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex Number();
}
