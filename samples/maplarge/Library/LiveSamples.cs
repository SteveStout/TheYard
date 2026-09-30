using System.Text.RegularExpressions;

namespace TestProject.Library;

/// <summary>
/// Fills in live code samples in a served document. A document may contain an empty fenced
/// code block whose info string reads <c>live path=Domain/HomePath.cs region=guard</c>.
/// When the document is requested, this replaces that block with an ordinary fenced block
/// holding the lines between <c>// #region guard</c> and its matching <c>// #endregion</c>
/// in that file, read from this build, so the sample can never drift from the real code.
/// <c>region=*</c> shows the whole file. A path outside the allowed folders, a missing file,
/// or a missing region produces a one-line note in the page instead of an error, so a renamed
/// region is visible to the reader rather than hidden in a log.
/// </summary>
public static partial class LiveSamples
{
    // #region allowed
    /// <summary>The only folders a live block may read, relative to the project root.</summary>
    public static readonly string[] AllowedRoots = ["Data/", "Domain/", "Application/", "Infrastructure/", "Controllers/", "Composition/", "Library/", "src/", "wwwroot/", "tests/", "docs/", "infra/"];

    /// <summary>The only files at the project root a live block may read.</summary>
    public static readonly string[] AllowedFiles = ["Program.cs", "TestProject.csproj", ".editorconfig", "Dockerfile", "appsettings.json"];

    /// <summary>
    /// Decides whether a live block may read a path, using string checks only, before any
    /// file is opened. The path must use plain characters and forward slashes, must have no
    /// empty, "." or ".." segment, and must be one of the allowed files or sit under one of
    /// the allowed folders. This keeps a document from reading anything outside the project
    /// or any file that is not on these lists.
    /// </summary>
    /// <param name="path">The path from the code block's info string.</param>
    public static bool IsAllowedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !PlainPath().IsMatch(path))
        {
            return false;
        }
        if (path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }
        return AllowedFiles.Contains(path, StringComparer.Ordinal)
            || AllowedRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal) && path.Length > root.Length);
    }
    // #endregion allowed

    /// <summary>
    /// Replaces every live block in the markdown with the current code sample, or with a note
    /// when the sample cannot be shown. All other lines pass through unchanged.
    /// </summary>
    /// <param name="markdown">The document as written.</param>
    /// <param name="projectRoot">The folder that holds the project's files.</param>
    public static string Expand(string markdown, string projectRoot)
    {
        string[] lines = markdown.Split('\n');
        var output = new List<string>(lines.Length + 32);
        for (int i = 0; i < lines.Length; i++)
        {
            var open = OpenFence().Match(lines[i].TrimEnd('\r'));
            if (!open.Success)
            {
                output.Add(lines[i]);
                continue;
            }
            // Skips ahead to the closing fence. A live block is meant to be empty, so any text
            // written inside it is dropped rather than shown next to the real code.
            int close = i + 1;
            while (close < lines.Length && !lines[close].TrimEnd('\r').StartsWith("```", StringComparison.Ordinal))
            {
                close++;
            }
            output.AddRange(Sample(Attributes(open.Groups["attrs"].Value), projectRoot));
            i = close;
        }
        return string.Join('\n', output);
    }

    /// <summary>
    /// Returns the path and region of every live block in a document, so a test can check that
    /// each one points at code that exists. A block without a region counts as "*".
    /// </summary>
    /// <param name="markdown">The document as written.</param>
    public static IEnumerable<(string Path, string Region)> Fences(string markdown)
    {
        foreach (string line in markdown.Split('\n'))
        {
            var open = OpenFence().Match(line.TrimEnd('\r'));
            if (open.Success)
            {
                var attrs = Attributes(open.Groups["attrs"].Value);
                yield return (attrs.GetValueOrDefault("path", string.Empty), attrs.GetValueOrDefault("region", "*"));
            }
        }
    }

    // #region sample
    private static List<string> Sample(Dictionary<string, string> attrs, string projectRoot)
    {
        string path = attrs.GetValueOrDefault("path", string.Empty);
        string region = attrs.GetValueOrDefault("region", "*");
        if (!IsAllowedPath(path))
        {
            return [$"> Sample unavailable: `{path}` is not a path a live block may read."];
        }
        string file = Path.GetFullPath(Path.Combine(projectRoot, path));
        if (!File.Exists(file))
        {
            return [$"> Sample unavailable: `{path}` is not in this build."];
        }
        string[] source = File.ReadAllLines(file);
        IReadOnlyList<string> body = region == "*" ? source : Region(source, region);
        if (body.Count == 0)
        {
            return [$"> Sample unavailable: `{path}` has no region named `{region}`."];
        }
        var block = new List<string>(body.Count + 3) { $"```{LanguageFor(path)} {path}" };
        block.AddRange(Dedent(body));
        block.Add("```");
        return block;
    }

    /// <summary>
    /// Returns the lines between a named region marker and its matching end marker, without the
    /// markers themselves. Returns an empty list when the region is not found or never closes.
    /// </summary>
    /// <param name="source">The file's lines.</param>
    /// <param name="name">The region's name.</param>
    public static IReadOnlyList<string> Region(string[] source, string name)
    {
        int start = Array.FindIndex(source, line => RegionStart().Match(line) is { Success: true } m && m.Groups["name"].Value == name);
        if (start < 0)
        {
            return [];
        }
        // Regions can be nested, so this counts every start and end marker from the named start.
        // The matching end is the one that brings the count back to zero.
        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            if (RegionStart().IsMatch(source[i]))
            {
                depth++;
            }
            else if (RegionEnd().IsMatch(source[i]))
            {
                depth--;
                if (depth == 0)
                {
                    return source[(start + 1)..i];
                }
            }
        }
        return [];
    }
    // #endregion sample

    /// <summary>
    /// Returns the code block language name for a file, based on its extension, so the page can
    /// apply syntax colouring. Unknown extensions get plain text.
    /// </summary>
    /// <param name="path">A file path.</param>
    public static string LanguageFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".ts" => "typescript",
        ".js" or ".mjs" => "javascript",
        ".css" => "css",
        ".html" => "html",
        ".json" => "json",
        ".md" => "markdown",
        ".yml" or ".yaml" => "yaml",
        ".bicep" => "bicep",
        ".csproj" => "xml",
        _ => "text",
    };

    private static IEnumerable<string> Dedent(IReadOnlyList<string> lines)
    {
        int indent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        return lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart());
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0)
            {
                attrs[pair[..eq]] = pair[(eq + 1)..];
            }
        }
        return attrs;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_./-]+$")]
    private static partial Regex PlainPath();

    [GeneratedRegex(@"^```live(?:[ \t]+(?<attrs>.*))?$")]
    private static partial Regex OpenFence();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])#region[ \t]+(?<name>[A-Za-z0-9_.-]+)")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])#endregion")]
    private static partial Regex RegionEnd();
}
