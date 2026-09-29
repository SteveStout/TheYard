using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The shape the composition root was split into (ADR: The composition root, split by
/// job): Program.cs reads as a table of contents under its line ceiling, maps no
/// endpoint itself, every route is mapped from a file under Endpoints/ or
/// Composition/, and every class in those two folders is static.
/// </summary>
public class CompositionRootTests
{
    // #region composition-root-rules
    /// <summary>The line ceiling Program.cs is held under.</summary>
    private const int Ceiling = 80;

    /// <summary>A call that maps a route, on anything: MapGet, MapPost, MapFallbackToFile and the rest.</summary>
    private static readonly Regex MapsARoute = new(
        @"\.Map(Get|Post|Put|Delete|Patch|Methods|Fallback\w*|Group|OpenApi|ScalarApiReference|HealthChecks)\(",
        RegexOptions.Compiled);

    private static string Api() => Path.Combine(Repo.Root(), "api", "TheYard.Api");

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Api(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(Api(), path).Replace('\\', '/');

    [Fact]
    public void Program_cs_is_a_table_of_contents_under_the_ceiling()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Api(), "Program.cs"));
        Assert.True(lines.Length < Ceiling, $"Program.cs is {lines.Length} lines; the ceiling is {Ceiling}");
    }

    [Fact]
    public void Program_cs_maps_no_route_itself()
    {
        string program = File.ReadAllText(Path.Combine(Api(), "Program.cs"));
        var direct = MapsARoute.Matches(program).Select(match => match.Value).ToList();
        Assert.True(direct.Count == 0, "Program.cs maps routes itself: " + string.Join(", ", direct));
    }

    [Fact]
    public void Every_route_is_mapped_from_a_file_under_Endpoints_or_Composition()
    {
        var outside = Sources()
            .Select(Relative)
            .Where(path => !path.StartsWith("Endpoints/", StringComparison.Ordinal)
                && !path.StartsWith("Composition/", StringComparison.Ordinal))
            .Where(path => MapsARoute.IsMatch(File.ReadAllText(Path.Combine(Api(), path))))
            .ToList();
        Assert.True(outside.Count == 0, "these map routes outside Endpoints/ and Composition/: " + string.Join(", ", outside));
    }

    [Fact]
    public void Every_class_in_Endpoints_and_Composition_is_static()
    {
        // Records are the wire shapes an endpoint binds or answers with, and the
        // settings a registration reads; a class there has no state to hold.
        var declaration = new Regex(@"^\s*public\s+((?:\w+\s+)*)class\s+(\w+)", RegexOptions.Multiline);
        var open = Sources()
            .Select(Relative)
            .Where(path => path.StartsWith("Endpoints/", StringComparison.Ordinal) || path.StartsWith("Composition/", StringComparison.Ordinal))
            .SelectMany(path => declaration.Matches(File.ReadAllText(Path.Combine(Api(), path)))
                .Where(match => !match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("static"))
                .Select(match => $"{path}: {match.Groups[2].Value}"))
            .ToList();
        Assert.True(open.Count == 0, "these classes are not static: " + string.Join(", ", open));
        Assert.Contains(Sources(), path => Relative(path).StartsWith("Endpoints/", StringComparison.Ordinal));
    }
    // #endregion composition-root-rules
}
