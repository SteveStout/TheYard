using System.Text.RegularExpressions;

namespace TheYard.Rules;

// #region summary-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70):
/// every public, internal or protected declaration a reader meets carries a <c>///</c> summary. The
/// count of the ones that do not is a ratchet number, which says how far there is to go; this rule is
/// the yes-or-no question underneath it, asked of the projects whose documentation is finished, so a
/// project paid down cannot quietly fill up again while the total still falls.
/// </summary>
/// <remarks>
/// The declaration pattern and the walk back over what may sit between a summary and the thing it
/// describes are the ones <c>.github/quality.py</c> uses for <c>undocumented_declarations</c>, kept the
/// same deliberately: if the two ever disagree, a project this rule calls finished would still be
/// counted by the ratchet, and the number and the rule would be describing different codebases. A
/// constant is left out of both, because its name and its value are on the line a reader is already
/// looking at.
/// </remarks>
public static class SummaryRule
{
    /// <summary>
    /// A declaration a reader meets: a type, or a member with an access word in front of it. The
    /// alternation ends with the shape of a member - a return type, a name, and then a bracket, a brace,
    /// an arrow or a semicolon - which is how a property, a method and a field are all caught by one
    /// pattern without naming any of them.
    /// </summary>
    private static readonly Regex Declaration = new(
        @"^\s*(public|internal|protected)\s+(?:(?:static|sealed|abstract|partial|readonly|async|override|virtual|required|new|record|unsafe)\s+)*"
        + @"(class|record|struct|interface|enum|[\w<>\[\],?. ]+?\s+\w+\s*(\(|\{|=>|;))",
        RegexOptions.Compiled);

    /// <summary>
    /// The lines that may sit between a summary and the declaration it describes without breaking the
    /// pair: an attribute, a region marker, and blank space.
    /// </summary>
    private static bool Skippable(string line)
    {
        string text = line.Trim();
        return text.Length == 0 || text.StartsWith('[') || text.StartsWith("// #region", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every declaration in one file's lines that carries no summary, as its line number (counted from
    /// one, the way an editor counts) and the text of the line.
    /// </summary>
    /// <param name="lines">The file's lines.</param>
    public static List<(int Line, string Text)> UndocumentedIn(IReadOnlyList<string> lines)
    {
        var found = new List<(int, string)>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!Declaration.IsMatch(lines[i])
                || lines[i].TrimStart().StartsWith("public const", StringComparison.Ordinal)
                || lines[i].TrimStart().StartsWith("internal const", StringComparison.Ordinal))
            {
                continue;
            }

            int j = i - 1;
            while (j >= 0 && Skippable(lines[j]))
            {
                j--;
            }

            if (j < 0 || !lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                found.Add((i + 1, lines[i].Trim()));
            }
        }

        return found;
    }

    /// <summary>
    /// How many declarations the rule read across the files it was given, so a scan that read nothing
    /// cannot pass for a scan that found nothing.
    /// </summary>
    /// <param name="files">The files.</param>
    public static int Declarations(IEnumerable<string> files) =>
        files.Sum(path => File.ReadAllLines(path).Count(line =>
            Declaration.IsMatch(line)
            && !line.TrimStart().StartsWith("public const", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("internal const", StringComparison.Ordinal)));

    /// <summary>
    /// Every undocumented declaration across the files given, as a line naming the file, the line and
    /// the declaration, which is what a reader needs to go and write the summary.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="repoRoot">The repository's root, for the paths the lines print.</param>
    public static List<string> Undocumented(IEnumerable<string> files, string repoRoot) =>
        [.. files.SelectMany(path => UndocumentedIn(File.ReadAllLines(path))
            .Select(found => $"{Path.GetRelativePath(repoRoot, path).Replace('\\', '/')}:{found.Line} {found.Text} has no summary"))];
}
// #endregion summary-rule
