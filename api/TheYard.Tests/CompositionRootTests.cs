using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The shape of the host (ADR: The onion cut into vertical slices, which rewrote the rules of ADR:
/// The composition root, split by job): Program.cs is a loop under its line ceiling and maps no
/// route itself; every route is mapped from a slice under Features/ or from the one fallback in
/// Shared/; every slice folder has a Module; and every class that registers or maps is static,
/// while a module is a sealed class the loop can find.
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
    public void Program_cs_is_a_loop_under_the_ceiling()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Api(), "Program.cs"));
        Assert.True(lines.Length < Ceiling, $"Program.cs is {lines.Length} lines; the ceiling is {Ceiling}");
        string program = string.Join('\n', lines);
        Assert.Contains("Modules.InThisHost()", program, StringComparison.Ordinal);
        Assert.Contains("module.Register(", program, StringComparison.Ordinal);
        Assert.Contains("module.Map(", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_cs_maps_no_route_itself()
    {
        string program = File.ReadAllText(Path.Combine(Api(), "Program.cs"));
        var direct = MapsARoute.Matches(program).Select(match => match.Value).ToList();
        Assert.True(direct.Count == 0, "Program.cs maps routes itself: " + string.Join(", ", direct));
    }

    [Fact]
    public void Every_route_is_mapped_from_a_slice_or_the_one_shared_fallback()
    {
        var outside = Sources()
            .Select(Relative)
            .Where(path => !path.StartsWith("Features/", StringComparison.Ordinal) && path != "Shared/Wiring/SpaRegistration.cs")
            .Where(path => MapsARoute.IsMatch(File.ReadAllText(Path.Combine(Api(), path))))
            .ToList();
        Assert.True(outside.Count == 0, "these map routes outside Features/ and the fallback: " + string.Join(", ", outside));
    }

    [Fact]
    public void Every_slice_folder_has_a_module_and_every_module_is_sealed()
    {
        string features = Path.Combine(Api(), "Features");
        var slices = Directory.EnumerateDirectories(features, "*", SearchOption.AllDirectories)
            .Where(folder => Directory.EnumerateFiles(folder, "*.cs").Any())
            .Select(folder => Path.GetRelativePath(features, folder).Replace('\\', '/'))
            .ToList();
        Assert.NotEmpty(slices);
        var withoutAModule = slices.Where(slice => !File.Exists(Path.Combine(features, slice, "Module.cs"))).ToList();
        Assert.True(withoutAModule.Count == 0, "these slices have no Module.cs: " + string.Join(", ", withoutAModule));
        var modules = typeof(Program).Assembly.GetTypes().Where(type => type.IsClass && typeof(TheYard.Api.Shared.Wiring.IModule).IsAssignableFrom(type)).ToList();
        Assert.Equal(slices.Count, modules.Count);
        Assert.All(modules, module => Assert.True(module.IsSealed && module.Name == "Module", $"{module.FullName} should be a sealed class named Module"));
    }

    [Fact]
    public void Every_class_that_registers_or_maps_is_static()
    {
        // A registration step and an endpoint class hold no state; a slice's Module is the one instance
        // class beside them. Every file under Shared/Wiring/ is a registration step or the settings one
        // reads (ADR-114), so the rule holds there whole. Anywhere else under Shared/ or Features/ it is
        // held for a file that maps or registers (it names the route builder or the service collection):
        // the shared concerns' services, the hosted services under Shared/Hosted/ and the guest sites under
        // Shared/Sites/ are running things with their dependencies in their constructors. Records are
        // the wire shapes and the settings a registration reads.
        var declaration = new Regex(@"^\s*public\s+((?:\w+\s+)*)class\s+(\w+)", RegexOptions.Multiline);
        var open = Sources()
            .Select(Relative)
            .Where(path => path.StartsWith("Shared/Wiring/", StringComparison.Ordinal)
                || ((path.StartsWith("Features/", StringComparison.Ordinal) || path.StartsWith("Shared/", StringComparison.Ordinal)) && !path.EndsWith("/Module.cs", StringComparison.Ordinal)
                    && Regex.IsMatch(File.ReadAllText(Path.Combine(Api(), path)), @"\bIEndpointRouteBuilder\b|\bIServiceCollection\b|\.Map(Get|Post|Put|Delete|Patch|Group)\(")))
            .SelectMany(path => declaration.Matches(File.ReadAllText(Path.Combine(Api(), path)))
                .Where(match => !match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("static"))
                .Select(match => $"{path}: {match.Groups[2].Value}"))
            .ToList();
        Assert.True(open.Count == 0, "these classes are not static: " + string.Join(", ", open));
        Assert.Contains(Sources(), path => Relative(path).StartsWith("Features/", StringComparison.Ordinal));
    }
    // #endregion composition-root-rules
}
