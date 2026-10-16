using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestProject.Tests;

/// <summary>
/// Checks the versions record against the project files: every package the tests and the page
/// use has a row with the version in use, the images match the Dockerfile, and every row that is
/// behind says why. (more in docs/ADR-014-technology-versions.md)
/// </summary>
public sealed partial class TechnologyVersionsTests
{
    /// <summary>The record's rows by package name: the version in use, the newest stable one, and the reason it is behind.</summary>
    private static Dictionary<string, (string InUse, string Latest, string Why)> Rows()
    {
        string record = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "docs", "ADR-014-technology-versions.md"));
        var rows = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in TableRow().Matches(record))
        {
            string package = row.Groups[1].Value.Trim();
            if (package is not "Package" && !package.StartsWith("---", StringComparison.Ordinal))
            {
                rows[package] = (row.Groups[2].Value.Trim(), row.Groups[3].Value.Trim(), row.Groups[4].Value.Trim());
            }
        }
        return rows;
    }

    [Fact]
    public void Every_package_the_project_uses_has_a_row_with_its_version()
    {
        var rows = Rows();
        Assert.NotEmpty(rows);
        var wrong = new List<string>();
        foreach (string project in Directory.EnumerateFiles(ProjectFolder.Root(), "*.csproj", SearchOption.AllDirectories).Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("obj")))
        {
            foreach (Match reference in PackageReference().Matches(File.ReadAllText(project)))
            {
                string package = reference.Groups[1].Value;
                string version = reference.Groups[2].Value;
                if (!rows.TryGetValue(package, out var row) || row.InUse != version)
                {
                    wrong.Add($"{Path.GetFileName(project)} uses {package} {version}");
                }
            }
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectFolder.Root(), "package.json")));
        foreach (var dependency in manifest.RootElement.GetProperty("devDependencies").EnumerateObject())
        {
            string version = dependency.Value.GetString()!.TrimStart('^', '~');
            if (!rows.TryGetValue(dependency.Name, out var row) || row.InUse != version)
            {
                wrong.Add($"package.json uses {dependency.Name} {version}");
            }
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void The_images_are_the_ones_the_Dockerfile_uses()
    {
        string tag = Rows()[".NET SDK and ASP.NET Core runtime images"].InUse;
        string dockerfile = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "Dockerfile"));
        Assert.Contains($"dotnet/sdk:{tag} ", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"dotnet/aspnet:{tag} ", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_that_is_behind_says_why()
    {
        List<string> unexplained = Rows()
            .Where(row => row.Value.InUse != row.Value.Latest && row.Value.Why.Length == 0)
            .Select(row => row.Key)
            .ToList();
        Assert.Empty(unexplained);
    }

    /// <summary>A table row of four cells.</summary>
    [GeneratedRegex(@"^\| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \| ?([^|]*?) ?\|$", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    /// <summary>A package reference with its version.</summary>
    [GeneratedRegex(@"<PackageReference Include=""([^""]+)"" Version=""([^""]+)""")]
    private static partial Regex PackageReference();
}
