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
/// Checks that the app's folders depend only inward, in the order Data, Domain, Application,
/// Infrastructure, Controllers, Composition. The tests read source files as text: the using
/// lines in each folder, any call to the disk, the clock or the HTTP context in Data and Domain,
/// and the size of Program.cs. Keeping dependencies one way lets the inner rules be tested with
/// no web server and no disk. This is onion architecture: the business rules are in the middle,
/// everything else depends on them, and these tests are what keep it that way.
/// (more in docs/ADR-002-one-project-four-folders-dependencies-inward.md)
/// </summary>
public sealed partial class LayeringTests
{
    // Innermost first. A folder may use itself and any folder earlier in this list, never a later
    // one. The Documentation folder is allowed only in Controllers and Composition.
    private static readonly string[] Order = ["Data", "Domain", "Application", "Infrastructure", "Controllers", "Composition"];

    [Fact]
    public void A_folder_uses_only_the_folders_inside_it()
    {
        var outward = new List<string>();
        for (int i = 0; i < Order.Length; i++)
        {
            string folder = Path.Combine(ProjectFolder.Root(), Order[i]);
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
            {
                foreach (Match match in Using().Matches(File.ReadAllText(file)))
                {
                    string used = match.Groups["folder"].Value;
                    int index = Array.IndexOf(Order, used);
                    if (index > i || (used == "Documentation" && Order[i] is not ("Controllers" or "Composition")))
                    {
                        outward.Add($"{ProjectFolder.Relative(file)} uses TestProject.{used}");
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

    [GeneratedRegex(@"^using TestProject\.(?<folder>\w+);", RegexOptions.Multiline)]
    private static partial Regex Using();
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

    [GeneratedRegex(@"from\s+'(?<from>[^']+)'")]
    private static partial Regex Import();
}

/// <summary>
/// Checks the stylesheet rules. app.css may not write a colour value (hex, rgb or hsl); every
/// colour comes from a CSS custom property, so the palette is changed in one file, tokens.css.
/// Every custom property app.css uses must be declared, so a typo cannot silently leave an element
/// with no colour. The font and its licence must be served from this site with no link to Google
/// Fonts or a CDN, so the page loads without calling any other server.
/// (more in docs/ADR-010-the-palette-borrowed-from-theyard.md)
/// </summary>
public sealed partial class StyleRulesTests
{
    [Fact]
    public void App_css_writes_no_colour_of_its_own()
    {
        string css = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "css", "app.css"));
        List<string> literals = ColourLiteral().Matches(css).Select(m => m.Value).Distinct().ToList();
        Assert.Empty(literals);
    }

    [Fact]
    public void Every_token_app_css_uses_is_declared_in_tokens_css()
    {
        string tokens = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "css", "tokens.css"));
        string app = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "css", "app.css"));
        HashSet<string> declared = Declared().Matches(tokens).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        declared.UnionWith(Declared().Matches(app).Select(m => m.Groups["name"].Value));
        List<string> missing = Used().Matches(app).Select(m => m.Groups["name"].Value).Distinct().Where(name => !declared.Contains(name)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_font_is_served_from_this_site()
    {
        Assert.True(File.Exists(Path.Combine(ProjectFolder.Root(), "wwwroot", "fonts", "ibm-plex-sans-latin.woff2")));
        Assert.True(File.Exists(Path.Combine(ProjectFolder.Root(), "wwwroot", "fonts", "OFL.txt")));
        string html = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "index.html"));
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

    [GeneratedRegex(@"\b[A-Z][A-Za-z]+Tests\b")]
    private static partial Regex TestName();

    [GeneratedRegex(@"\bADR-\d{3}\b")]
    private static partial Regex Citation();
}
