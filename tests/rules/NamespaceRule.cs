using System.Text.RegularExpressions;

namespace TheYard.Rules;

// #region namespace-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70): a C# file's
/// namespace is its folder. A file under a ring's folder declares the ring's namespace followed by the folders
/// between, so a reader finds a type where its namespace says it is. The site's suite and the job hunt's suite each
/// hand it their own folders.
/// </summary>
public static partial class NamespaceRule
{
    /// <summary>
    /// Every file under <paramref name="folder"/> whose declared namespace is not <paramref name="ringNamespace"/>
    /// followed by its folders, as a line naming the file, what it declares and what its folder says. Build output is
    /// skipped, and so is any file <paramref name="exempt"/> names, by its path relative to the folder.
    /// </summary>
    /// <param name="folder">The folder the namespace is counted from: a ring's project folder, or its Features folder.</param>
    /// <param name="ringNamespace">The namespace a file directly in that folder declares.</param>
    /// <param name="repoRoot">The repository's root, for the paths the lines print.</param>
    /// <param name="exempt">Files left out, with the reason their caller gives.</param>
    public static List<string> NotTheirFolder(string folder, string ringNamespace, string repoRoot, Func<string, bool>? exempt = null)
    {
        var wrong = new List<string>();
        foreach (string path in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)))
        {
            string relative = Path.GetRelativePath(folder, path);
            if (exempt?.Invoke(relative.Replace('\\', '/')) == true)
            {
                continue;
            }
            string? between = Path.GetDirectoryName(relative);
            string expected = string.IsNullOrEmpty(between) ? ringNamespace : ringNamespace + "." + between.Replace(Path.DirectorySeparatorChar, '.');
            string declared = Declared().Match(File.ReadAllText(path)).Groups[1].Value;
            if (declared != expected)
            {
                wrong.Add($"{Path.GetRelativePath(repoRoot, path).Replace('\\', '/')} declares {declared}, its folder says {expected}");
            }
        }
        return wrong;
    }

    /// <summary>True for a path inside a bin or obj folder.</summary>
    /// <param name="path">The path.</param>
    public static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(@"(?m)^namespace\s+([\w.]+)")]
    private static partial Regex Declared();
}
// #endregion namespace-rule
