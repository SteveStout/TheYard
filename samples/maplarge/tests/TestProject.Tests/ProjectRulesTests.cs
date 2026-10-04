using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject.Tests;

/// <summary>
/// Checks that no file in the project contains an em dash character. The test reads every source,
/// style, markup, document and config file as text and lists any file that holds one. The
/// project's writing style bans the character, and a test is the only way to keep a later change
/// from slipping one back in. (more in docs/STYLE.md)
/// </summary>
public sealed class NoEmDashTests
{
    // Built from its numeric code point, so this file never contains the character itself
    // and passes its own scan.
    /// <summary>The em dash, written by its code so this file can name it without carrying one.</summary>
    private const char EmDash = (char)0x2014;

    [Fact]
    public void No_file_we_wrote_contains_an_em_dash()
    {
        List<string> offenders = ProjectFolder.FilesWith(".cs", ".ts", ".js", ".css", ".html", ".md", ".json", ".csproj", ".yml", ".bicep", ".editorconfig")
            .Where(file => File.ReadAllText(file).Contains(EmDash))
            .Select(ProjectFolder.Relative)
            .ToList();
        Assert.Empty(offenders);
    }
}

/// <summary>
/// Checks that every class in the app is sealed, static or abstract. The first test loads the
/// compiled app by reflection and lists any top-level class that could still be inherited from.
/// The second reads .editorconfig and the project file to confirm that analyzer CA1852, which
/// flags an internal class that could be sealed, is a warning and that warnings fail the build.
/// Sealing by default means inheritance is always a deliberate choice, never an accident.
/// </summary>
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
        string editorconfig = File.ReadAllText(Path.Combine(ProjectFolder.Root(), ".editorconfig"));
        Assert.Contains("dotnet_diagnostic.CA1852.severity = warning", editorconfig, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", File.ReadAllText(Path.Combine(ProjectFolder.Root(), "TestProject.csproj")), StringComparison.Ordinal);
    }
}

/// <summary>
/// Checks the two layering rules that read best as text: Data and Domain never mention the disk,
/// the clock or the HTTP context, and Program.cs stays a short list that maps no route itself.
/// Which folder may use which is held by OnionTests, which reads the compiled app.
/// (more in docs/ADR-002-one-project-four-folders-dependencies-inward.md)
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void Data_and_Domain_touch_no_filesystem_and_no_clock()
    {
        var impure = new List<string>();
        foreach (string folder in new[] { "Data", "Domain" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(ProjectFolder.Root(), folder), "*.cs"))
            {
                string source = File.ReadAllText(file);
                foreach (string forbidden in new[] { "File.", "Directory.", "DateTime.Now", "DateTime.UtcNow", "HttpContext" })
                {
                    if (source.Contains(forbidden, StringComparison.Ordinal))
                    {
                        impure.Add($"{ProjectFolder.Relative(file)} mentions {forbidden}");
                    }
                }
            }
        }
        Assert.Empty(impure);
    }

    [Fact]
    public void Program_maps_nothing_itself_and_stays_short()
    {
        string[] lines = File.ReadAllLines(Path.Combine(ProjectFolder.Root(), "Program.cs"));
        Assert.True(lines.Length <= 40, $"Program.cs is {lines.Length} lines; the host is a table of contents");
        Assert.DoesNotContain(lines, line => line.Contains("MapGet(", StringComparison.Ordinal) || line.Contains("MapPost(", StringComparison.Ordinal));
    }
}

/// <summary>
/// Checks the rules for the browser code under src. Every TypeScript module must have its compiled
/// JavaScript committed under wwwroot/js, so the app runs with the .NET SDK alone and no Node build
/// step. Modules in src/lib must never import from src/ui, so the logic stays testable without a
/// page. No module may set innerHTML, because text from a file name or a document could then run
/// as markup; the page builds elements instead. The TypeScript compiler is the only npm package.
/// (more in docs/ADR-006-typescript-organised.md)
/// </summary>
public sealed partial class FrontEndRulesTests
{
    /// <summary>The TypeScript source folder.</summary>
    private static string Src => Path.Combine(ProjectFolder.Root(), "src");

    [Fact]
    public void Every_source_module_has_its_compiled_module_beside_the_page()
    {
        List<string> missing = Directory.EnumerateFiles(Src, "*.ts", SearchOption.AllDirectories)
            .Select(file => Path.ChangeExtension(Path.GetRelativePath(Src, file), ".js"))
            .Where(relative => !File.Exists(Path.Combine(ProjectFolder.Root(), "wwwroot", "js", relative)))
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Lib_never_imports_ui_and_ui_never_reaches_past_lib()
    {
        var outward = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Src, "lib"), "*.ts"))
        {
            foreach (Match match in Import().Matches(File.ReadAllText(file)))
            {
                if (match.Groups["from"].Value.Contains("/ui/", StringComparison.Ordinal))
                {
                    outward.Add($"{ProjectFolder.Relative(file)} imports {match.Groups["from"].Value}");
                }
            }
        }
        Assert.Empty(outward);
    }

    [Fact]
    public void No_source_uses_innerHTML_and_only_the_compiler_is_a_dependency()
    {
        List<string> offenders = Directory.EnumerateFiles(Src, "*.ts", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(".innerHTML", StringComparison.Ordinal))
            .Select(ProjectFolder.Relative)
            .ToList();
        Assert.Empty(offenders);
        string package = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "package.json"));
        Assert.DoesNotContain("\"dependencies\"", package, StringComparison.Ordinal);
        Assert.Contains("\"typescript\"", package, StringComparison.Ordinal);
    }

    /// <summary>The module path in an import line, so a rule can see which folder a file reaches into.</summary>
    [GeneratedRegex(@"from\s+'(?<from>[^']+)'")]
    private static partial Regex Import();
}

/// <summary>
/// Checks that the rules table in docs/ADR-009-the-rules-a-change-has-to-pass.md matches the code.
/// Every test class a table row names must exist in this assembly and hold at least one test, and
/// every ADR number the document cites must have a file in docs. A reader uses the table to find
/// the test behind each rule, so renaming or deleting a test must fail here, not leave a dead row.
/// </summary>
public sealed partial class RuleTableTests
{
    [Fact]
    public void Every_test_the_table_names_is_a_class_with_a_test_in_it()
    {
        string record = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "docs", "ADR-009-the-rules-a-change-has-to-pass.md"));
        // Keep only table rows, the lines that start with a pipe. The Files section below the
        // table lists RepoRulesTests.cs, which fits the name pattern but is a file, not a rule.
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
        string record = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "docs", "ADR-009-the-rules-a-change-has-to-pass.md"));
        var missing = new List<string>();
        foreach (Match cited in Citation().Matches(record))
        {
            if (!Directory.EnumerateFiles(Path.Combine(ProjectFolder.Root(), "docs"), $"{cited.Value}-*.md").Any())
            {
                missing.Add(cited.Value);
            }
        }
        Assert.Empty(missing);
    }

    /// <summary>A test class name in the rules table, a capitalised word ending in Tests.</summary>
    [GeneratedRegex(@"\b[A-Z][A-Za-z]+Tests\b")]
    private static partial Regex TestName();

    /// <summary>A record cited by number in the rules table.</summary>
    [GeneratedRegex(@"\bADR-\d{3}\b")]
    private static partial Regex Citation();
}
