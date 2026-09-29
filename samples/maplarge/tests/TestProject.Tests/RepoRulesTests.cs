using System.Reflection;
using System.Text.RegularExpressions;
using TestProject.Application;
using TestProject.Docs;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>Nothing in the project carries an em dash (ADR-009; the house voice, docs/STYLE.md).</summary>
public sealed class HouseVoiceTests
{
    // Written as a code point so this file passes its own scan.
    private const char EmDash = (char)0x2014;

    [Fact]
    public void No_file_we_wrote_contains_an_em_dash()
    {
        List<string> offenders = Repo.FilesWith(".cs", ".js", ".css", ".html", ".md", ".json", ".csproj", ".yml", ".bicep", ".editorconfig")
            .Where(file => File.ReadAllText(file).Contains(EmDash))
            .Select(Repo.Relative)
            .ToList();
        Assert.Empty(offenders);
    }
}

/// <summary>Every class is sealed, static or abstract (ADR-009; the practice is TheYard's docs/SEALED.md).</summary>
public sealed class SealedByDefaultTests
{
    [Fact]
    public void Every_class_in_the_app_is_sealed_static_or_abstract()
    {
        List<string> open = typeof(Program).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract && !type.IsNested)
            .Select(type => type.FullName!)
            .ToList();
        Assert.Empty(open);
    }

    [Fact]
    public void The_analyzer_that_holds_the_internal_half_is_a_warning()
    {
        string editorconfig = File.ReadAllText(Path.Combine(Repo.Root(), ".editorconfig"));
        Assert.Contains("dotnet_diagnostic.CA1852.severity = warning", editorconfig, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", File.ReadAllText(Path.Combine(Repo.Root(), "TestProject.csproj")), StringComparison.Ordinal);
    }
}

/// <summary>Dependencies point inward: Data, Domain, Application, Infrastructure, Controllers (ADR-002).</summary>
public sealed partial class LayeringTests
{
    // A folder may use itself and anything to its left, never anything to its right.
    private static readonly string[] Order = ["Data", "Domain", "Application", "Infrastructure", "Controllers"];

    [Fact]
    public void A_folder_uses_only_the_folders_inside_it()
    {
        var outward = new List<string>();
        for (int i = 0; i < Order.Length; i++)
        {
            string folder = Path.Combine(Repo.Root(), Order[i]);
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
            {
                foreach (Match match in Using().Matches(File.ReadAllText(file)))
                {
                    string used = match.Groups["folder"].Value;
                    int index = Array.IndexOf(Order, used);
                    if (index > i || (used == "Docs" && Order[i] != "Controllers"))
                    {
                        outward.Add($"{Repo.Relative(file)} uses TestProject.{used}");
                    }
                }
            }
        }
        Assert.Empty(outward);
    }

    [Fact]
    public void Data_and_Domain_touch_no_filesystem_and_no_clock()
    {
        var impure = new List<string>();
        foreach (string folder in new[] { "Data", "Domain" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(Repo.Root(), folder), "*.cs"))
            {
                string source = File.ReadAllText(file);
                foreach (string forbidden in new[] { "File.", "Directory.", "DateTime.Now", "DateTime.UtcNow", "HttpContext" })
                {
                    if (source.Contains(forbidden, StringComparison.Ordinal))
                    {
                        impure.Add($"{Repo.Relative(file)} mentions {forbidden}");
                    }
                }
            }
        }
        Assert.Empty(impure);
    }

    [Fact]
    public void Program_maps_nothing_itself_and_stays_short()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Repo.Root(), "Program.cs"));
        Assert.True(lines.Length <= 100, $"Program.cs is {lines.Length} lines; the host is a table of contents");
        Assert.DoesNotContain(lines, line => line.Contains("MapGet(", StringComparison.Ordinal) || line.Contains("MapPost(", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^using TestProject\.(?<folder>\w+);", RegexOptions.Multiline)]
    private static partial Regex Using();
}

/// <summary>No stylesheet but the token sheet writes a colour, and every token used is declared (ADR-010).</summary>
public sealed partial class StyleRulesTests
{
    [Fact]
    public void App_css_writes_no_colour_of_its_own()
    {
        string css = File.ReadAllText(Path.Combine(Repo.Root(), "wwwroot", "css", "app.css"));
        List<string> literals = ColourLiteral().Matches(css).Select(m => m.Value).Distinct().ToList();
        Assert.Empty(literals);
    }

    [Fact]
    public void Every_token_app_css_uses_is_declared_in_tokens_css()
    {
        string tokens = File.ReadAllText(Path.Combine(Repo.Root(), "wwwroot", "css", "tokens.css"));
        string app = File.ReadAllText(Path.Combine(Repo.Root(), "wwwroot", "css", "app.css"));
        HashSet<string> declared = Declared().Matches(tokens).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        declared.UnionWith(Declared().Matches(app).Select(m => m.Groups["name"].Value));
        List<string> missing = Used().Matches(app).Select(m => m.Groups["name"].Value).Distinct().Where(name => !declared.Contains(name)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_font_is_served_from_this_site()
    {
        Assert.True(File.Exists(Path.Combine(Repo.Root(), "wwwroot", "fonts", "ibm-plex-sans-latin.woff2")));
        Assert.True(File.Exists(Path.Combine(Repo.Root(), "wwwroot", "fonts", "OFL.txt")));
        string html = File.ReadAllText(Path.Combine(Repo.Root(), "wwwroot", "index.html"));
        Assert.DoesNotContain("fonts.googleapis", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cdn.", html, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(")]
    private static partial Regex ColourLiteral();

    [GeneratedRegex(@"(?<name>--[a-z0-9-]+)\s*:")]
    private static partial Regex Declared();

    [GeneratedRegex(@"var\((?<name>--[a-z0-9-]+)")]
    private static partial Regex Used();
}

/// <summary>The rules table in ADR-009 names tests that exist, and every record it cites exists (ADR-009).</summary>
public sealed partial class RuleTableTests
{
    [Fact]
    public void Every_test_the_table_names_is_a_class_with_a_test_in_it()
    {
        string record = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "ADR-009-the-rules-a-change-has-to-pass.md"));
        // Only the table's rows: a file name in the Files section (RepoRulesTests.cs) is not a rule.
        string table = string.Join('\n', record.Split('\n').Where(line => line.StartsWith('|')));
        List<string> named = TestName().Matches(table).Select(m => m.Value).Distinct().ToList();
        Assert.True(named.Count >= 8, $"the table names only {named.Count} tests");
        var missing = new List<string>();
        foreach (string name in named)
        {
            Type? type = Assembly.GetExecutingAssembly().GetType($"TestProject.Tests.{name}");
            bool hasTests = type is not null && type.GetMethods().Any(m => m.GetCustomAttributes(typeof(FactAttribute), true).Length > 0);
            if (!hasTests)
            {
                missing.Add(name);
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_record_the_table_cites_exists()
    {
        string record = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "ADR-009-the-rules-a-change-has-to-pass.md"));
        var missing = new List<string>();
        foreach (Match cited in Citation().Matches(record))
        {
            if (!Directory.EnumerateFiles(Path.Combine(Repo.Root(), "docs"), $"{cited.Value}-*.md").Any())
            {
                missing.Add(cited.Value);
            }
        }
        Assert.Empty(missing);
    }

    [GeneratedRegex(@"\b[A-Z][A-Za-z]+Tests\b")]
    private static partial Regex TestName();

    [GeneratedRegex(@"\bADR-\d{3}\b")]
    private static partial Regex Citation();
}

/// <summary>The path guard the live samples use is the same idea as the home's, on a shorter list (ADR-012).</summary>
public sealed class LiveSamplesTests
{
    [Theory]
    [InlineData("Domain/HomePath.cs", true)]
    [InlineData("Program.cs", true)]
    [InlineData("wwwroot/js/lib/urlState.js", true)]
    [InlineData("../secrets.txt", false)]
    [InlineData("Domain/../Program.cs", false)]
    [InlineData("appsettings.Development.json", false)]
    [InlineData("Domain/", false)]
    [InlineData("", false)]
    public void Only_a_plain_path_under_an_allowed_root_may_be_read(string path, bool allowed)
    {
        Assert.Equal(allowed, LiveSamples.IsAllowedPath(path));
    }

    [Fact]
    public void A_region_is_cut_between_its_markers_and_nested_regions_are_kept_whole()
    {
        string[] source =
        [
            "a",
            "// #region outer",
            "b",
            "// #region inner",
            "c",
            "// #endregion",
            "d",
            "// #endregion",
            "e",
        ];
        string[] expected1 = ["b", "// #region inner", "c", "// #endregion", "d"];
        Assert.Equal(expected1, LiveSamples.Region(source, "outer"));
        string[] expected2 = ["c"];
        Assert.Equal(expected2, LiveSamples.Region(source, "inner"));
        Assert.Empty(LiveSamples.Region(source, "absent"));
    }

    [Fact]
    public void A_fence_expands_to_the_region_and_a_bad_one_to_a_note()
    {
        string expanded = LiveSamples.Expand("before\n```live path=Domain/HomePath.cs region=guard\n```\nafter", Repo.Root());
        Assert.Contains("```csharp Domain/HomePath.cs", expanded, StringComparison.Ordinal);
        Assert.Contains("public string Resolve(string? relative)", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("#region guard", expanded, StringComparison.Ordinal);
        Assert.StartsWith("before\n", expanded, StringComparison.Ordinal);
        Assert.EndsWith("\nafter", expanded, StringComparison.Ordinal);
        string note = LiveSamples.Expand("```live path=../x.cs region=y\n```", Repo.Root());
        Assert.StartsWith("> Sample unavailable", note, StringComparison.Ordinal);
    }
}

/// <summary>The version is read from the changelog and the commit from .git, and each says "unknown" rather than guessing (ADR-012).</summary>
public sealed class VersionReaderTests
{
    [Fact]
    public void The_first_listed_version_wins_and_a_missing_log_is_unknown()
    {
        using var home = new TempHome();
        string log = home.File("CHANGELOG.md", "# Changelog\n\nintro 9.9.9.9 in prose is not a line\n\n- 1.0.0.3 newest.\n- 1.0.0.2 older.\n");
        Assert.Equal("1.0.0.3", VersionReader.VersionFrom(log));
        Assert.Equal(VersionReader.Unknown, VersionReader.VersionFrom(Path.Combine(home.Root, "missing.md")));
    }

    [Fact]
    public void A_folder_with_no_git_reports_unknown_and_a_ref_is_followed()
    {
        using var home = new TempHome();
        Assert.Equal(VersionReader.Unknown, VersionReader.CommitFrom(home.Root));
        home.File(".git/HEAD", "ref: refs/heads/main\n");
        home.File(".git/refs/heads/main", "0123456789abcdef0123456789abcdef01234567\n");
        Assert.Equal("0123456", VersionReader.CommitFrom(home.Root));
    }
}

/// <summary>The home directory's guard is the one the app runs with: Program builds it from the same options (ADR-003).</summary>
public sealed class HomeForTests
{
    [Fact]
    public void Empty_configuration_means_the_sample_home_beside_the_project()
    {
        using var root = new TempHome();
        HomePath home = Program.HomeFor(new FilesOptions(), root.Root);
        Assert.Equal(Path.Combine(root.Root, "sample-home"), home.Root);
        Assert.True(Directory.Exists(home.Root));
    }

    [Fact]
    public void An_absolute_home_is_used_as_given_and_a_relative_one_is_under_the_content_root()
    {
        using var root = new TempHome();
        Assert.Equal(root.Root, Program.HomeFor(new FilesOptions { Home = root.Root }, "/elsewhere").Root);
        Assert.Equal(Path.Combine(root.Root, "data"), Program.HomeFor(new FilesOptions { Home = "data" }, root.Root).Root);
    }
}
