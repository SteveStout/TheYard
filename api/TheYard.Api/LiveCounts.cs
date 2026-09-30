using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api;

/// <summary>
/// Live numbers for the served docs (ADR: The palette, the addendum on the Style
/// section becoming four pages). A document may write a live placeholder (two
/// braces around <c>live:</c> and a measure) anywhere, and this replaces it at request time with the number the
/// repository in this build holds, the way <see cref="LiveSamples"/> replaces a
/// live fence with the code: a count of the design tokens, of the facts in a
/// test class, of the lines in a file, of the files that carry the three-line
/// header. The count is made on the server because the browser cannot read a
/// C# file or list a folder; a number typed into a page is wrong the day the
/// code moves, and this one cannot be. An unknown measure renders as a note in
/// words, never as an error and never as a guessed number.
/// </summary>
public static partial class LiveCounts
{
    // #region live-counts
    /// <summary>The four design token files, in the order main.tsx loads them.</summary>
    public static readonly string[] DesignTokenFiles = ["src/styles/colors.css", "src/styles/sizes.css", "src/styles/typography.css", "src/styles/effects.css"];

    /// <summary>The two folders whose code carries the header, as FileHeaderTests reads them; every stylesheet under src carries it too.</summary>
    public static readonly string[] HeaderFolders = ["src/app", "src/library"];

    /// <summary>
    /// Replaces every live placeholder in <paramref name="markdown"/> with the
    /// measure read from <paramref name="repoRoot"/>. A measure that returns
    /// several lines (the fact names) is meant to sit alone on a line inside a
    /// fence, and replaces that line.
    /// </summary>
    public static string Expand(string markdown, string repoRoot) =>
        Placeholder().Replace(markdown, match => Measure(match.Groups["measure"].Value.Trim(), repoRoot));

    /// <summary>One measure, as text.</summary>
    public static string Measure(string measure, string repoRoot)
    {
        string[] words = measure.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string kind = words.Length > 0 ? words[0] : "";
        string? path = words.Length > 1 ? words[1] : null;
        try
        {
            return kind switch
            {
                "design-tokens" when path is null => Number(DesignTokenFiles.SelectMany(file => TokenNames(repoRoot, file)).Distinct(StringComparer.Ordinal).Count()),
                "design-tokens" when DesignTokenFiles.Contains(path) => Number(TokenNames(repoRoot, path!).Distinct(StringComparer.Ordinal).Count()),
                "design-token-files" => Number(DesignTokenFiles.Count(file => File.Exists(Full(repoRoot, file)))),
                "facts" when Readable(path) => Number(FactAttribute().Matches(File.ReadAllText(Full(repoRoot, path!))).Count),
                "fact-names" when Readable(path) => string.Join("\n", FactName().Matches(File.ReadAllText(Full(repoRoot, path!))).Select(match => match.Groups["name"].Value)),
                "lines" when Readable(path) => Number(File.ReadAllLines(Full(repoRoot, path!)).Length),
                "sheets" => Number(Sheets(repoRoot).Count),
                "headers" => $"{Number(HeaderScope(repoRoot).Count(file => HasHeader(file)))} of {Number(HeaderScope(repoRoot).Count)}",
                "over-300" => Number(HeaderScope(repoRoot).Count(file => File.ReadAllLines(file).Length > 300)),
                "array" when path is not null => Number(ArrayLength(repoRoot, "src/lib/ribbons.ts", path)),
                "documents" => Number(DocumentationCatalog.Files.Count),
                _ => $"(no live measure named '{measure}')",
            };
        }
        catch (IOException)
        {
            return $"(the file for '{measure}' is not in this build)";
        }
    }
    // #endregion live-counts

    private static string Number(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static string Full(string repoRoot, string path) => Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A path a measure may read: the same rule a live fence follows.</summary>
    private static bool Readable(string? path) => LiveSamples.IsAllowedPath(path);

    /// <summary>Every custom property a design token file declares, comments left out.</summary>
    private static IEnumerable<string> TokenNames(string repoRoot, string file) =>
        Declaration().Matches(Comment().Replace(File.ReadAllText(Full(repoRoot, file)), ""))
            .Select(match => match.Groups["name"].Value);

    /// <summary>Every stylesheet under src.</summary>
    private static List<string> Sheets(string repoRoot)
    {
        string src = Full(repoRoot, "src");
        return Directory.Exists(src) ? Directory.EnumerateFiles(src, "*.css", SearchOption.AllDirectories).ToList() : [];
    }

    /// <summary>The files FileHeaderTests holds: the code in the two folders, and every stylesheet under src. Tests are left out.</summary>
    private static List<string> HeaderScope(string repoRoot) =>
        HeaderFolders
            .Select(folder => Full(repoRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".ts", StringComparison.Ordinal) || file.EndsWith(".tsx", StringComparison.Ordinal))
            .Concat(Sheets(repoRoot))
            .Where(file => !file.EndsWith(".test.ts", StringComparison.Ordinal) && !file.EndsWith(".test.tsx", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The header's shape, as FileHeaderTests reads it: the first comment, with the three labels in order.</summary>
    private static bool HasHeader(string file)
    {
        string[] lines = File.ReadLines(file).Take(20).ToArray();
        if (lines.Length == 0 || lines[0].Trim() != "/**")
        {
            return false;
        }

        int close = Array.FindIndex(lines, line => line.Trim() == "*/");
        string head = string.Join("\n", close < 0 ? lines : lines[..close]);
        int does = head.IndexOf("* Does:", StringComparison.Ordinal);
        int doesNot = head.IndexOf("* Does not:", StringComparison.Ordinal);
        int usedBy = head.IndexOf("* Used by:", StringComparison.Ordinal);
        return close > 0 && does > 0 && doesNot > does && usedBy > doesNot;
    }

    /// <summary>The entries of one exported array literal in a data file, one a line.</summary>
    private static int ArrayLength(string repoRoot, string file, string name)
    {
        string text = File.ReadAllText(Full(repoRoot, file));
        var array = Regex.Match(text, @"export const " + Regex.Escape(name) + @"\b[^=]*=\s*\[(?<body>.*?)\n\];", RegexOptions.Singleline);
        return array.Success ? Regex.Matches(array.Groups["body"].Value, @"^\s*\[", RegexOptions.Multiline).Count : 0;
    }

    [GeneratedRegex(@"\{\{live:(?<measure>[^}]+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\s*\[(?:Fact|Theory)\b", RegexOptions.Multiline)]
    private static partial Regex FactAttribute();

    [GeneratedRegex(@"^\s*\[(?:Fact|Theory)\b[^\n]*\n\s*public\s+(?:async\s+)?\S+\s+(?<name>\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex FactName();

    [GeneratedRegex(@"(?:^|[;{\s])(?<name>--[a-z0-9-]+)\s*:")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();
}
