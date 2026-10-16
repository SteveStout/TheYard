using System.Text.RegularExpressions;

namespace TestProject.Tests;

/// <summary>
/// Checks the stylesheet rules. Only tokens.css may write a colour value (hex, rgb or hsl); every
/// other sheet takes its colours from CSS custom properties, so the palette is changed in one
/// file. Every custom property a sheet uses must be declared, so a typo cannot silently leave an
/// element with no colour. Every sheet closes each brace it opens, because one unclosed media
/// query limits every rule after it to phones with no error anywhere. The font and its licence must be served
/// from this site with no link to Google Fonts or a CDN, so the page loads without calling any
/// other server. (more in docs/ADR-010-the-palette-borrowed-from-theyard.md)
/// </summary>
public sealed partial class StyleRulesTests
{
    /// <summary>The folder of stylesheets the page links.</summary>
    private static string Sheets => Path.Combine(ProjectFolder.Root(), "wwwroot", "css");

    /// <summary>Every stylesheet except tokens.css, the one place a colour value may be written.</summary>
    private static IEnumerable<string> PartSheets() =>
        Directory.EnumerateFiles(Sheets, "*.css").Where(file => Path.GetFileName(file) != "tokens.css");

    [Fact]
    public void No_sheet_but_tokens_css_writes_a_colour_of_its_own()
    {
        List<string> literals = PartSheets()
            .SelectMany(file => ColourLiteral().Matches(File.ReadAllText(file)).Select(m => $"{Path.GetFileName(file)}: {m.Value}"))
            .Distinct()
            .ToList();
        Assert.Empty(literals);
    }

    [Fact]
    public void Every_token_a_sheet_uses_is_declared()
    {
        HashSet<string> declared = Directory.EnumerateFiles(Sheets, "*.css")
            .SelectMany(file => Declared().Matches(File.ReadAllText(file)).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);
        List<string> missing = PartSheets()
            .SelectMany(file => Used().Matches(File.ReadAllText(file)).Select(m => (file, name: m.Groups["name"].Value)))
            .Where(use => !declared.Contains(use.name))
            .Select(use => $"{Path.GetFileName(use.file)}: {use.name}")
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_sheet_closes_each_brace_it_opens_and_the_page_links_every_sheet()
    {
        string html = File.ReadAllText(Path.Combine(ProjectFolder.Root(), "wwwroot", "index.html"));
        var wrong = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Sheets, "*.css"))
        {
            string css = File.ReadAllText(file);
            int opens = css.Count(c => c == '{'), closes = css.Count(c => c == '}');
            if (opens != closes)
            {
                wrong.Add($"{Path.GetFileName(file)} opens {opens} braces and closes {closes}");
            }
            if (!html.Contains($"href=\"/css/{Path.GetFileName(file)}\"", StringComparison.Ordinal))
            {
                wrong.Add($"{Path.GetFileName(file)} is not linked from index.html");
            }
        }
        Assert.Empty(wrong);
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

    /// <summary>A colour written as a value (hex, rgb or hsl) rather than taken from a token.</summary>
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(")]
    private static partial Regex ColourLiteral();

    /// <summary>A design token being declared, the name before its colon.</summary>
    [GeneratedRegex(@"(?<name>--[a-z0-9-]+)\s*:")]
    private static partial Regex Declared();

    /// <summary>A design token being used inside var().</summary>
    [GeneratedRegex(@"var\((?<name>--[a-z0-9-]+)")]
    private static partial Regex Used();
}
