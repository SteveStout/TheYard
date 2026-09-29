using System.Text.RegularExpressions;
using TestProject.Data;

namespace TestProject.Library;

/// <summary>
/// The documents the app serves, read from the <c>docs</c> folder beside the
/// project at request time so a record can never go stale against the code it
/// describes (ADR-012). The sidebar order is decided here: start here, the
/// README, the decision records by number, then the guides.
/// </summary>
public sealed partial class DocsCatalog(string contentRoot)
{
    /// <summary>The sidebar group a record sits in.</summary>
    public const string RecordsGroup = "Decision records";

    /// <summary>The sidebar group the guides sit in.</summary>
    public const string GuidesGroup = "Guides";

    private static readonly string[] Guides = ["STYLE", "BUILT-WITH-AI", "CHANGELOG"];

    /// <summary>The folder the records are read from.</summary>
    public string DocsFolder { get; } = Path.Combine(contentRoot, "docs");

    // #region catalogue
    /// <summary>Every document, in the order the sidebar shows them.</summary>
    public IReadOnlyList<DocEntry> List()
    {
        var entries = new List<DocEntry>
        {
            new("start-here", TitleOf(Path.Combine(DocsFolder, "START-HERE.md")), "Start here"),
            new("readme", TitleOf(Path.Combine(contentRoot, "README.md")), "Start here"),
        };
        foreach (string file in Directory.EnumerateFiles(DocsFolder, "ADR-*.md").OrderBy(RecordNumber))
        {
            entries.Add(new DocEntry(SlugOf(file), TitleOf(file), RecordsGroup));
        }
        foreach (string guide in Guides)
        {
            entries.Add(new DocEntry(guide.ToLowerInvariant(), TitleOf(Path.Combine(DocsFolder, guide + ".md")), GuidesGroup));
        }
        return entries;
    }

    /// <summary>The file a slug names, or null. A slug is a lower-case file name, which is the only shape the catalogue makes.</summary>
    /// <param name="slug">The address the request asked for.</param>
    public string? FileFor(string slug)
    {
        if (slug == "readme")
        {
            return Path.Combine(contentRoot, "README.md");
        }
        return List().Any(entry => entry.Slug == slug)
            ? Directory.EnumerateFiles(DocsFolder, "*.md").FirstOrDefault(file => SlugOf(file) == slug)
            : null;
    }
    // #endregion catalogue

    /// <summary>The number in a record's file name, so ADR-010 sorts after ADR-009 and not after ADR-001.</summary>
    /// <param name="file">A path ending in ADR-NNN-anything.md.</param>
    public static int RecordNumber(string file)
    {
        var match = Number().Match(Path.GetFileName(file));
        return match.Success ? int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
    }

    private static string SlugOf(string file) => Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

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

    [GeneratedRegex(@"^ADR-(?<n>\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex Number();
}
