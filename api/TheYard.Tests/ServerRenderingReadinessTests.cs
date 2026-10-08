using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// What is already in place for drawing the site's pages on a server, held so it stays in place
/// (ADR: The landing page rendered at build time, server rendering as the goal). The goal is a
/// rendering service of its own, beside the API and apart from it, asking the API for data like
/// any other client. So the API host draws no React page and runs no JavaScript: no Razor, no
/// single-page-app or Node hosting package, no Node process, and the built page reaches a visitor
/// as a file. The other half, that every component the landing page renders draws with no
/// browser, is held by src/app/drawLanding.test.ts.
/// </summary>
public class ServerRenderingReadinessTests
{
    private static string ApiFolder => Path.Combine(Repo.Root(), "api", "TheYard.Api");

    // #region host-draws-no-react
    [Fact]
    public void The_api_host_carries_no_page_framework_and_no_javascript_runtime()
    {
        string project = File.ReadAllText(Path.Combine(ApiFolder, "TheYard.Api.csproj"));
        Assert.StartsWith("<Project Sdk=\"Microsoft.NET.Sdk.Web\">", project.TrimStart(), StringComparison.Ordinal);
        foreach (string package in new[] { "Razor", "SpaServices", "NodeServices", "Jering.Javascript", "React", "ClearScript", "Jint" })
        {
            Assert.DoesNotMatch(new Regex($@"<PackageReference\s+Include=""[^""]*{Regex.Escape(package)}", RegexOptions.IgnoreCase), project);
        }

        var pageFiles = Directory.EnumerateFiles(ApiFolder, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(pageFiles.Count == 0, "the API host holds page templates: " + string.Join(", ", pageFiles));
    }

    [Fact]
    public void The_api_host_never_starts_node_and_serves_the_built_page_as_a_file()
    {
        var code = Directory.EnumerateFiles(ApiFolder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (path, text: File.ReadAllText(path)))
            .ToList();
        var startsNode = code.Where(file => Regex.IsMatch(file.text, @"Process\.Start\([^)]*node|FileName\s*=\s*""node", RegexOptions.IgnoreCase)).Select(file => file.path).ToList();
        Assert.True(startsNode.Count == 0, "the API host starts Node: " + string.Join(", ", startsNode));
        var draws = code.Where(file => Regex.IsMatch(file.text, @"AddRazorPages|AddRazorComponents|AddControllersWithViews|MapRazorPages|UseSpa\(|UseReactDevelopmentServer")).Select(file => file.path).ToList();
        Assert.True(draws.Count == 0, "the API host draws pages: " + string.Join(", ", draws));

        // The page every app route answers with is the built index.html, as a file.
        string spa = File.ReadAllText(Path.Combine(ApiFolder, "Composition", "SpaRegistration.cs"));
        Assert.Contains("MapFallbackToFile(", spa, StringComparison.Ordinal);
    }
    // #endregion host-draws-no-react
}
