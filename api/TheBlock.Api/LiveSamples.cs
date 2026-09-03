using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBlock.Api;

/// <summary>
/// Live code samples for the served docs (ADR-014). A markdown doc may hold an
/// empty fenced block whose info string reads
/// <c>live path=src/components/DocsMenu.tsx region=MENU_ORDER</c>, and this
/// expands it at request time into an ordinary fenced block holding the
/// current lines between the matching <c>#region NAME</c> and
/// <c>#endregion</c> comment pair in that file, read from this build;
/// <c>region=*</c> shows the whole file, for the files that cannot carry a
/// marker (package.json, the tsconfig files). Paths are confined to a short list of repo roots and checked as strings before
/// any filesystem touch; anything missing renders a one-line note, never an
/// error.
/// </summary>
public static partial class LiveSamples
{
    // #region whitelist
    /// <summary>The only roots a live block may read from, relative to the repo root.</summary>
    public static readonly string[] AllowedRoots = ["src/", "api/", "infra/", ".github/", "tests/", "edge/"];

    /// <summary>The single files at the repo root a live block may read: the ones the records decide (ADR-017).</summary>
    public static readonly string[] AllowedFiles = ["Dockerfile", "netlify.toml", "playwright.config.ts", "vite.config.ts", "package.json", "index.html", "tsconfig.json", "tsconfig.app.json", "tsconfig.node.json", ".editorconfig"];

    /// <summary>
    /// A relative path is allowed when it is plain (letters, digits, dot, dash,
    /// underscore, forward slashes), starts under an allowed root, and has no
    /// parent-directory segment anywhere. Pure string checks: no filesystem.
    /// </summary>
    // NotNullWhen is the whole fix for the one nullable warning this build
    // carried: the guard inside already proves path is not null when it
    // returns true, and the attribute is how that proof reaches the caller.
    public static bool IsAllowedPath([NotNullWhen(true)] string? path)
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
    // #endregion

    private const string RepoUrl = "https://github.com/SteveStout/TheYard";

    [GeneratedRegex(@"^[A-Za-z0-9_./-]+$")]
    private static partial Regex PlainPath();

    /// <summary>The opening fence of a live block: three backticks, "live", then key=value attributes.</summary>
    [GeneratedRegex(@"^```live(?:[ \t]+(?<attrs>.*))?$")]
    private static partial Regex OpenFence();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])#region[ \t]+(?<name>[A-Za-z0-9_.-]+)")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])#endregion(?:[ \t]+(?<name>[A-Za-z0-9_.-]+))?")]
    private static partial Regex RegionEnd();

    // #region expander
    /// <summary>
    /// Replaces every live block in <paramref name="markdown"/> with the current
    /// sample, or with a one-line note when the sample cannot be shown. Lines
    /// outside live blocks pass through untouched.
    /// </summary>
    public static string Expand(string markdown, string repoRoot, string commit)
    {
        string[] lines = markdown.Split('\n');
        var output = new List<string>(lines.Length + 16);
        for (int i = 0; i < lines.Length; i++)
        {
            var open = OpenFence().Match(lines[i].TrimEnd('\r'));
            if (!open.Success)
            {
                output.Add(lines[i]);
                continue;
            }
            int close = i + 1;
            while (close < lines.Length && lines[close].TrimEnd('\r') != "```")
            {
                close++;
            }
            if (close >= lines.Length)
            {
                // An unterminated block is left alone rather than guessed at.
                output.Add(lines[i]);
                continue;
            }
            var attrs = ParseAttributes(open.Groups["attrs"].Value);
            output.Add(Render(attrs.GetValueOrDefault("path"), attrs.GetValueOrDefault("region"), repoRoot, commit));
            i = close;
        }
        return string.Join('\n', output);
    }
    // #endregion

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq > 0)
            {
                attrs[token[..eq]] = token[(eq + 1)..];
            }
        }
        return attrs;
    }

    /// <summary>One sample: the fenced block plus a line saying where it came from.</summary>
    private static string Render(string? path, string? region, string repoRoot, string commit)
    {
        if (!IsAllowedPath(path))
        {
            return Note($"`{path ?? "(no path)"}` is outside the allowed roots (src/, api/, infra/, .github/, tests/, edge/, or a named root file).");
        }
        if (string.IsNullOrWhiteSpace(region))
        {
            return Note($"no region named for `{path}`.");
        }

        string rootFull = Path.GetFullPath(repoRoot);
        string fileFull = Path.GetFullPath(Path.Combine(rootFull, path));
        if (!fileFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !fileFull.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Note($"`{path}` resolves outside the repo.");
        }
        if (!File.Exists(fileFull))
        {
            return Note($"`{path}` is not in this build.");
        }

        string[] source;
        try
        {
            source = File.ReadAllLines(fileFull);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Note($"`{path}` could not be read.");
        }

        // #region whole-file
        // region=* is the whole file, for files that cannot carry a comment marker
        // (package.json, the tsconfig files); everything else is a marked region.
        bool wholeFile = region == "*";
        int start = wholeFile ? -1 : Array.FindIndex(source, line =>
        {
            var m = RegionStart().Match(line);
            return m.Success && m.Groups["name"].Value == region;
        });
        // A named end marker wins, so regions may nest; a bare #endregion closes the nearest open one.
        int end = wholeFile ? source.Length : -1;
        if (start >= 0)
        {
            end = Array.FindIndex(source, start + 1, line =>
            {
                var m = RegionEnd().Match(line);
                return m.Success && m.Groups["name"].Value == region;
            });
            if (end < 0)
            {
                end = Array.FindIndex(source, start + 1, line =>
                {
                    var m = RegionEnd().Match(line);
                    return m.Success && m.Groups["name"].Value.Length == 0;
                });
            }
        }
        if (!wholeFile && (start < 0 || end < 0))
        {
            return Note($"region `{region}` was not found in `{path}`.");
        }
        // #endregion whole-file

        var body = source[(start + 1)..end].ToList();
        while (body.Count > 0 && string.IsNullOrWhiteSpace(body[^1]))
        {
            body.RemoveAt(body.Count - 1);
        }
        while (body.Count > 0 && string.IsNullOrWhiteSpace(body[0]))
        {
            body.RemoveAt(0);
            start++;
        }
        if (body.Count == 0)
        {
            return Note($"region `{region}` in `{path}` is empty.");
        }
        int indent = body.Where(l => !string.IsNullOrWhiteSpace(l)).Min(l => l.Length - l.TrimStart().Length);
        var code = body.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart());

        string reference = commit is "local" or "" ? "main" : commit;
        string url = $"{RepoUrl}/blob/{reference}/{path}#L{start + 2}-L{end}";
        var sb = new StringBuilder();
        sb.Append("```").Append(LanguageFor(path)).Append('\n');
        sb.Append(string.Join('\n', code)).Append('\n');
        sb.Append("```\n");
        sb.Append($"*Live from [`{path}`]({url}), region `{region}`, read from this build at {reference}.*");
        return sb.ToString();
    }

    private static string Note(string reason) => $"*Sample unavailable: {reason}*";

    private static string LanguageFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ts" => "ts",
        ".tsx" => "tsx",
        ".cs" => "csharp",
        ".css" => "css",
        ".yml" or ".yaml" => "yaml",
        ".bicep" => "bicep",
        ".json" => "json",
        ".md" => "markdown",
        ".csproj" or ".slnx" or ".sqlproj" => "xml",
        ".sql" => "sql",
        _ => "text",
    };
}
