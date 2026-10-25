using System.Text.RegularExpressions;

namespace TheYard.Rules;

// #region modular-monolith-rule
/// <summary>
/// The modular monolith's rules (ADR-119), one body compiled into both test projects by a link: each process is
/// one deployable cut into modules, the modules are the vertical slices, a slice names no other slice, and the
/// two processes share rings and linked source files, never a project or a deployable.
/// </summary>
public static partial class ModularMonolithRule
{
    /// <summary>The projects under a folder that deploy on their own: a web host, an executable or a Functions app.</summary>
    /// <param name="folder">A process's folder (api or functions).</param>
    /// <param name="repoRoot">The repository's root, for the paths returned.</param>
    public static List<string> Deployables(string folder, string repoRoot) =>
        [.. Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !NamespaceRule.IsBuildOutput(path))
            .Where(path =>
            {
                string text = File.ReadAllText(path);
                return text.Contains("Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.Ordinal)
                    || text.Contains("<OutputType>Exe</OutputType>", StringComparison.Ordinal)
                    || text.Contains("<AzureFunctionsVersion>", StringComparison.Ordinal);
            })
            .Where(path => !IsTestProject(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Every using directive in a file under <paramref name="ringFolder"/>/Features/&lt;Slice&gt;/ that names another
    /// slice of any ring: a namespace that starts with one of <paramref name="ringNamespaces"/>, then ".Features.",
    /// then a slice whose name is not the file's own.
    /// </summary>
    /// <param name="ringFolder">One ring's project folder.</param>
    /// <param name="ringNamespaces">Every ring's root namespace in the process.</param>
    /// <param name="repoRoot">The repository's root, for the paths returned.</param>
    public static List<string> CrossSliceUsings(string ringFolder, IReadOnlyList<string> ringNamespaces, string repoRoot)
    {
        string features = Path.Combine(ringFolder, "Features");
        if (!Directory.Exists(features))
        {
            return [];
        }
        var wrong = new List<string>();
        foreach (string path in Directory.EnumerateFiles(features, "*.cs", SearchOption.AllDirectories).Where(path => !NamespaceRule.IsBuildOutput(path)))
        {
            string own = Path.GetRelativePath(features, path).Split(Path.DirectorySeparatorChar)[0];
            foreach (Match used in Using().Matches(File.ReadAllText(path)))
            {
                string name = used.Groups["ns"].Value;
                string? slice = ringNamespaces
                    .Select(ring => ring + ".Features.")
                    .Where(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(prefix => name[prefix.Length..].Split('.')[0])
                    .FirstOrDefault();
                if (slice is not null && slice != own)
                {
                    wrong.Add($"{Path.GetRelativePath(repoRoot, path).Replace('\\', '/')} (slice {own}) uses {name}");
                }
            }
        }
        return wrong;
    }

    /// <summary>Every project reference from a project under <paramref name="from"/> to a project under <paramref name="to"/>.</summary>
    /// <param name="from">One process's folder.</param>
    /// <param name="to">The other process's folder.</param>
    /// <param name="repoRoot">The repository's root, for the paths returned.</param>
    public static List<string> ProjectReferencesAcross(string from, string to, string repoRoot)
    {
        string target = Path.GetFullPath(to) + Path.DirectorySeparatorChar;
        var wrong = new List<string>();
        foreach (string project in Directory.EnumerateFiles(from, "*.csproj", SearchOption.AllDirectories).Where(path => !NamespaceRule.IsBuildOutput(path)))
        {
            foreach (Match reference in ProjectReference().Matches(File.ReadAllText(project)))
            {
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (resolved.StartsWith(target, StringComparison.Ordinal))
                {
                    wrong.Add($"{Path.GetRelativePath(repoRoot, project).Replace('\\', '/')} references {Path.GetRelativePath(repoRoot, resolved).Replace('\\', '/')}");
                }
            }
        }
        return wrong;
    }

    /// <summary>Every source file a project under <paramref name="from"/> compiles by a link from under <paramref name="to"/>, as a path relative to the root.</summary>
    /// <param name="from">One process's folder.</param>
    /// <param name="to">The other process's folder, or the shared tests folder.</param>
    /// <param name="repoRoot">The repository's root, for the paths returned.</param>
    public static List<string> LinkedFrom(string from, string to, string repoRoot)
    {
        string target = Path.GetFullPath(to) + Path.DirectorySeparatorChar;
        var linked = new List<string>();
        foreach (string project in Directory.EnumerateFiles(from, "*.csproj", SearchOption.AllDirectories).Where(path => !NamespaceRule.IsBuildOutput(path)))
        {
            foreach (Match include in LinkedCompile().Matches(File.ReadAllText(project)))
            {
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar)));
                if (resolved.StartsWith(target, StringComparison.Ordinal))
                {
                    linked.Add(Path.GetRelativePath(repoRoot, resolved).Replace('\\', '/'));
                }
            }
        }
        return [.. linked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static bool IsTestProject(string text) =>
        text.Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal) || text.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal);

    [GeneratedRegex(@"(?m)^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?<ns>[\w.]+)\s*;")]
    private static partial Regex Using();

    [GeneratedRegex(@"<ProjectReference\s+Include=""(?<path>[^""]+)""")]
    private static partial Regex ProjectReference();

    [GeneratedRegex(@"<Compile\s+Include=""(?<path>[^""]+)""\s+Link=")]
    private static partial Regex LinkedCompile();
}
// #endregion modular-monolith-rule
