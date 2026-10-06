using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The technology versions record says which version of each package the site uses, the newest
/// stable one on the day it was read, and why when the two differ (ADR: Technology versions).
/// This holds the "In use" column to the project files, so the page cannot say one version while
/// the build uses another, and holds every row that is behind to a written reason.
/// </summary>
public class TechnologyVersionsTests
{
    // #region versions
    /// <summary>One row of the record's tables: the package, the version in use, the newest stable one, and why it is behind.</summary>
    private sealed record Row(string Package, string InUse, string Latest, string Why);

    private static Dictionary<string, Row> Rows()
    {
        string record = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "decisions", "ADR-089-technology-versions.md"));
        var rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Regex.Matches(record, @"^\| ([^|]+?) \| ([^|]+?) \| ([^|]+?) \| ?([^|]*?) ?\|$", RegexOptions.Multiline))
        {
            string package = row.Groups[1].Value.Trim();
            if (package is "Package" || package.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }
            rows[package] = new Row(package, row.Groups[2].Value.Trim(), row.Groups[3].Value.Trim(), row.Groups[4].Value.Trim());
        }
        return rows;
    }

    [Fact]
    public void Every_dotnet_package_reference_has_a_row_with_the_version_in_use()
    {
        var rows = Rows();
        Assert.NotEmpty(rows);
        var wrong = new List<string>();
        foreach (string project in Directory.EnumerateFiles(Path.Combine(Repo.Root(), "api"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match reference in Regex.Matches(File.ReadAllText(project), @"<PackageReference Include=""([^""]+)"" Version=""([^""]+)"""))
            {
                string package = reference.Groups[1].Value;
                string version = reference.Groups[2].Value;
                if (!rows.TryGetValue(package, out var row))
                {
                    wrong.Add($"{Path.GetFileName(project)} uses {package} {version}, which the record does not list");
                }
                else if (row.InUse != version)
                {
                    wrong.Add($"{Path.GetFileName(project)} uses {package} {version} and the record says {row.InUse}");
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_front_end_package_has_a_row_with_the_version_in_use()
    {
        var rows = Rows();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), "package.json")));
        var wrong = new List<string>();
        foreach (string section in new[] { "dependencies", "devDependencies" })
        {
            foreach (var dependency in manifest.RootElement.GetProperty(section).EnumerateObject())
            {
                string version = dependency.Value.GetString()!.TrimStart('^', '~');
                if (!rows.TryGetValue(dependency.Name, out var row))
                {
                    wrong.Add($"package.json uses {dependency.Name} {version}, which the record does not list");
                }
                else if (row.InUse != version)
                {
                    wrong.Add($"package.json uses {dependency.Name} {version} and the record says {row.InUse}");
                }
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_build_tools_in_the_record_are_the_ones_the_build_uses()
    {
        var rows = Rows();
        string root = Repo.Root();
        string sqlProject = File.ReadAllText(Path.Combine(root, "api", "TheYard.Database", "TheYard.Database.sqlproj"));
        Assert.Contains($"Microsoft.Build.Sql/{rows["Microsoft.Build.Sql"].InUse}\"", sqlProject, StringComparison.Ordinal);

        string node = rows["node (Docker build stage and CI)"].InUse;
        Assert.Contains($"FROM node:{node}-alpine", File.ReadAllText(Path.Combine(root, "Dockerfile")), StringComparison.Ordinal);
        string ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.DoesNotMatch(new Regex(@"node-version: (?!" + node + @"\b)\d+"), ci);
    }

    [Fact]
    public void Every_row_that_is_behind_says_why()
    {
        var unexplained = Rows().Values
            .Where(row => row.InUse != row.Latest && row.Why.Length == 0)
            .Select(row => $"{row.Package} is at {row.InUse} with {row.Latest} out and no reason given")
            .ToList();
        Assert.True(unexplained.Count == 0, string.Join(Environment.NewLine, unexplained));
    }
    // #endregion versions
}
