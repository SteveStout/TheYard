using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The Style section's four pages (ADR: The palette, the addendum on the four
/// pages): each is served and in the sidebar, their words keep the section's
/// rules (no em dash, "design token" and never a bare "token", and none of the
/// words the section is not written at), every live number on them is one the
/// build can count, and every tile and glossary link lands on a page and a
/// section that exist.
/// </summary>
public class StyleSectionTests
{
    // #region style-section
    /// <summary>The four pages, by slug, in the order the sidebar lists them.</summary>
    private static readonly string[] Pages = ["style-guide", "color-style", "background-ribbon", "ui-architecture"];

    private static string Root => Repo.Root();

    private static string Markdown(string slug) => File.ReadAllText(Path.Combine(Root, DocumentationCatalog.Files[slug]));

    /// <summary>A page's own words: every fenced block left out, since a fence is code or data, and inline code left out.</summary>
    private static string Prose(string markdown)
    {
        string withoutFences = Regex.Replace(markdown, @"^```[^\n]*\n.*?^```\s*$", "", RegexOptions.Singleline | RegexOptions.Multiline);
        return Regex.Replace(withoutFences, "`[^`\n]*`", "");
    }

    [Fact]
    public void The_four_pages_are_served_and_are_the_Style_sections_rows()
    {
        string sections = File.ReadAllText(Path.Combine(Root, "src", "library", "sections.ts"));
        string look = Regex.Match(sections, @"look: \{.*?\n  \},", RegexOptions.Singleline).Value;
        string pages = File.ReadAllText(Path.Combine(Root, "src", "library", "pages.ts"));
        var keys = Regex.Matches(look, @"key: '(\w+)'").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(["styleGuide", "colorStyle", "backgroundRibbon", "uiArchitecture"], keys);
        foreach (string slug in Pages)
        {
            Assert.True(DocumentationCatalog.Files.ContainsKey(slug), $"{slug} is not in DocumentationCatalog.Files");
            Assert.True(File.Exists(Path.Combine(Root, DocumentationCatalog.Files[slug])), $"{DocumentationCatalog.Files[slug]} does not exist");
            Assert.Contains($"url: '/api/docs/{slug}'", pages, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_pages_say_design_token_never_a_bare_token_and_carry_no_em_dash()
    {
        var wrong = new List<string>();
        foreach (string slug in Pages)
        {
            string markdown = Markdown(slug);
            string prose = Prose(markdown);
            if (markdown.Contains((char)0x2014))
            {
                wrong.Add($"{slug} carries an em dash");
            }

            foreach (Match bare in Regex.Matches(prose, @"(?<![Dd]esign[ -])\b[Tt]okens?\b"))
            {
                wrong.Add($"{slug} says a bare '{bare.Value}' at {bare.Index}: write 'design token', since a bare token reads as the AI kind on this site");
            }

            foreach (Match word in Regex.Matches(prose, @"hiring manager|recruiter", RegexOptions.IgnoreCase))
            {
                wrong.Add($"{slug} says '{word.Value}': these pages speak through what the build holds, not to a reader by title");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_design_token_files_comments_say_design_token()
    {
        var wrong = new List<string>();
        foreach (string file in LiveCounts.DesignTokenFiles)
        {
            string text = File.ReadAllText(Path.Combine(Root, file));
            foreach (Match comment in Regex.Matches(text, @"/\*.*?\*/", RegexOptions.Singleline))
            {
                // A region marker is a name, not a sentence: sheet-tokens is what live fences ask for.
                string words = Regex.Replace(comment.Value, @"#(?:end)?region\s+\S+", "");
                foreach (Match bare in Regex.Matches(words, @"(?<![Dd]esign[ -])\b[Tt]okens?\b"))
                {
                    wrong.Add($"{file} says a bare '{bare.Value}' in: {comment.Value[..Math.Min(80, comment.Value.Length)]}");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_live_number_on_the_pages_is_one_the_build_counts()
    {
        var wrong = new List<string>();
        foreach (string slug in Pages)
        {
            string expanded = LiveCounts.Expand(Markdown(slug), Root);
            if (expanded.Contains("{{live:", StringComparison.Ordinal) || expanded.Contains("(no live measure", StringComparison.Ordinal)
                || expanded.Contains("is not in this build", StringComparison.Ordinal))
            {
                wrong.Add($"{slug} has a live number the build cannot count");
            }

            if (LiveSamples.Expand(Markdown(slug), Root, "local").Contains("Sample unavailable", StringComparison.Ordinal))
            {
                wrong.Add($"{slug} has a live fence whose file or region is not there");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_readouts_are_the_numbers_the_repository_holds()
    {
        int facts = typeof(StyleRulesTests).GetMethods().Count(method => method.GetCustomAttributes(typeof(FactAttribute), false).Length > 0);
        Assert.Equal(facts.ToString(System.Globalization.CultureInfo.InvariantCulture), LiveCounts.Measure("facts api/TheYard.Tests/StyleRulesTests.cs", Root));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in LiveCounts.DesignTokenFiles)
        {
            string css = Regex.Replace(File.ReadAllText(Path.Combine(Root, file)), @"/\*.*?\*/", "", RegexOptions.Singleline);
            names.UnionWith(Regex.Matches(css, @"(--[a-z0-9-]+)\s*:").Select(match => match.Groups[1].Value));
        }
        Assert.Equal(names.Count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture), LiveCounts.Measure("design-tokens", Root));
        Assert.Equal("4", LiveCounts.Measure("design-token-files", Root));

        string[] headers = LiveCounts.Measure("headers", Root).Split(" of ");
        Assert.Equal(headers[1], headers[0]);
        Assert.Equal("70", LiveCounts.Measure("array SPARKS", Root));
    }

    [Fact]
    public void Every_tile_and_glossary_link_lands_on_a_page_and_a_section_that_exist()
    {
        string guide = Markdown("style-guide");
        var tiles = Regex.Match(guide, @"```tiles\n(.*?)```", RegexOptions.Singleline).Groups[1].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('|')[0].Trim()).ToList();
        Assert.Equal(["color-style", "background-ribbon", "ui-architecture"], tiles);

        var wrong = new List<string>();
        string glossary = Regex.Match(guide, @"```glossary\n(.*?)```", RegexOptions.Singleline).Groups[1].Value;
        var entries = glossary.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.True(entries.Count >= 12, $"the glossary holds {entries.Count} terms");
        foreach (string entry in entries)
        {
            string[] target = entry.Split('|')[3].Trim().Split('#');
            if (!Pages.Contains(target[0]))
            {
                wrong.Add($"{entry.Split('|')[0].Trim()} links to {target[0]}, which is not a Style page");
                continue;
            }

            var sections = Regex.Matches(Markdown(target[0]), @"^## (.+)$", RegexOptions.Multiline).Select(match => Slug(match.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
            if (target.Length < 2 || !sections.Contains(target[1]))
            {
                wrong.Add($"{entry.Split('|')[0].Trim()} links to {string.Join('#', target)}, and that page has no section with that id");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>A heading's id, the way src/lib/markdown.ts's withHeadingIds makes it.</summary>
    private static string Slug(string heading) =>
        Regex.Replace(Regex.Replace(heading.ToLowerInvariant().Replace("`", ""), "[^a-z0-9 -]", "").Trim(), " +", "-");
    // #endregion style-section
}
