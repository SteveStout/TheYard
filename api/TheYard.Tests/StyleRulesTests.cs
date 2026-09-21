using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The colour and style rules, held by the gate (ADR: The palette, the addendum
/// on the style section). The rules are written on a page a person reads,
/// docs/COLOR-STYLE.md, and a rule on a page is followed until the first
/// session that does not read the page. Each rule here fails with a sentence
/// that says what to do, so a change that breaks one learns the rule from the
/// failure.
///
/// <para>Two rules are held elsewhere and are not repeated: every pairing the
/// site makes is measured in src/styles/tokens.test.ts, and that nothing which
/// is read is faded is held against the rendered page by tests/e2e/glass.spec.ts.
/// The last fact here holds that those two are still there.</para>
/// </summary>
public class StyleRulesTests
{
    // #region the sheet and the page
    private static string Root => Repo.Root();

    private static string TokenSheet() =>
        File.ReadAllText(Path.Combine(Root, "src", "styles", "tokens.css"));

    private static string StylePage() =>
        File.ReadAllText(Path.Combine(Root, "docs", "COLOR-STYLE.md"));

    private static readonly Regex HexToken =
        new(@"(--[a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\s*;", RegexOptions.Compiled);

    /// <summary>Every token the sheet gives a six-digit value, by name.</summary>
    private static Dictionary<string, string> Tokens()
    {
        // The first value a name is given is the token. The fallbacks under the
        // sheet give --glass-bg a second and a third, and those are fallbacks.
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in HexToken.Matches(TokenSheet()))
        {
            tokens.TryAdd(match.Groups[1].Value, match.Groups[2].Value.ToLowerInvariant());
        }

        return tokens;
    }

    /// <summary>Source with its comments taken out, so a rule about code is not
    /// broken by a sentence about code.</summary>
    private static string WithoutComments(string source)
    {
        string blocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(blocks, @"(?m)(^|\s)//.*$", " ");
    }

    /// <summary>The stylesheets and components this site writes, the token
    /// sheet and the tests left out.</summary>
    private static IEnumerable<string> StyledSource() =>
        Repo.FilesWith(".css", ".tsx", ".ts")
            .Where(path => path.Contains(Path.Combine(Root, "src") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.EndsWith("tokens.css", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".test.ts", StringComparison.Ordinal)
                && !path.EndsWith(".test.tsx", StringComparison.Ordinal));
    // #endregion the sheet and the page

    // #region contrast
    private static double Channel(string hex, int offset)
    {
        double value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        string h = hex.TrimStart('#');
        return (0.2126 * Channel(h, 0)) + (0.7152 * Channel(h, 2)) + (0.0722 * Channel(h, 4));
    }

    /// <summary>The WCAG contrast ratio, the same arithmetic as src/lib/contrast.ts.</summary>
    private static double Contrast(string a, string b)
    {
        double first = Luminance(a);
        double second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    /// <summary>One colour laid over another at a share, as a hex, rounded the
    /// way a screen rounds it.</summary>
    private static string Over(string top, string bottom, double share)
    {
        string t = top.TrimStart('#');
        string b = bottom.TrimStart('#');
        var parts = new[] { 0, 2, 4 }.Select(at =>
        {
            int above = int.Parse(t.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int below = int.Parse(b.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (int)Math.Round((above * share) + (below * (1 - share)), MidpointRounding.AwayFromZero);
        });
        return "#" + string.Concat(parts.Select(part => part.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string Figure(double ratio) => ratio.ToString("0.00", CultureInfo.InvariantCulture);
    // #endregion contrast

    // #region rule one
    /// <summary>
    /// The places a raw colour is right, by file, each with its reason. There
    /// are none today: the last four, a white, a hairline and two scrims, became
    /// tokens when this rule was written. A looser rule would be shorter, and
    /// would stop being a rule.
    /// </summary>
    private static readonly Dictionary<string, string> RawColourAllowed = new(StringComparer.Ordinal);

    [Fact]
    public void No_stylesheet_or_component_carries_a_raw_colour()
    {
        var raw = new Regex(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(", RegexOptions.Compiled);
        var found = new List<string>();
        int read = 0;

        foreach (string path in StyledSource())
        {
            read++;
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            if (RawColourAllowed.ContainsKey(relative))
            {
                continue;
            }

            string[] lines = WithoutComments(File.ReadAllText(path)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in raw.Matches(lines[i]))
                {
                    found.Add(
                        $"{relative} has the raw colour '{match.Value}': give it a token in src/styles/tokens.css, "
                        + "list the token on docs/COLOR-STYLE.md, and use var(--the-token) here");
                }
            }
        }

        Assert.True(read > 30, $"only {read} files were read, so this scan is reading the wrong folder");
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Take(20)));
    }
    // #endregion rule one

    // #region rule two
    [Fact]
    public void Every_hex_on_the_style_page_is_a_tokens_value_and_every_colour_token_is_on_the_page()
    {
        var tokens = Tokens();
        string page = StylePage();
        var values = tokens.Values.ToHashSet(StringComparer.Ordinal);
        var wrong = new List<string>();

        foreach (Match match in Regex.Matches(page, @"#[0-9a-fA-F]{6}\b"))
        {
            if (!values.Contains(match.Value.ToLowerInvariant()))
            {
                wrong.Add(
                    $"docs/COLOR-STYLE.md states {match.Value}, which is the value of no token in src/styles/tokens.css: "
                    + "the page describes the sheet, so change the page to the sheet's value or name the colour in words");
            }
        }

        foreach (string name in tokens.Keys.Where(name => name.StartsWith("--color-", StringComparison.Ordinal)))
        {
            if (!Regex.IsMatch(page, Regex.Escape(name) + @"(?![a-z0-9-])"))
            {
                wrong.Add(
                    $"{name} is in src/styles/tokens.css and not on docs/COLOR-STYLE.md: "
                    + "add it to the swatches fence of the section it belongs to, with the name a person calls it");
            }
        }

        Assert.True(tokens.Count > 40, $"only {tokens.Count} tokens were read from the sheet");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule two

    // #region rule three
    /// <summary>
    /// Every pairing the page states in words: the two tokens, and the bar the
    /// pairing has to clear (4.5 text, 3.0 marks, 0 where the page states a
    /// distance and not a floor).
    /// </summary>
    private static readonly (string Above, string Below, double Floor)[] Pairings =
    [
        ("--color-on-accent", "--color-accent", 4.5),
        ("--color-accent", "--color-surface", 4.5),
        ("--color-accent", "--color-bg", 4.5),
        ("--color-on-accent", "--color-accent-hover", 4.5),
        ("--color-heading", "--color-accent-soft", 4.5),
        ("--color-accent", "--color-accent-soft", 4.5),
        ("--color-green-dark", "--color-surface", 3.0),
        ("--color-green-dark", "--color-bg", 3.0),
        ("--color-on-accent", "--color-teal-deep", 4.5),
        ("--color-on-accent", "--color-green-dark", 4.5),
        ("--color-on-accent", "--color-teal-header", 4.5),
        ("--color-gold-light", "--color-teal-header", 4.5),
        ("--color-gold-light", "--color-green-dark", 4.5),
        ("--color-text", "--color-surface", 4.5),
        ("--color-text", "--color-bg", 4.5),
        ("--color-heading", "--color-surface", 4.5),
        ("--color-heading", "--color-bg", 4.5),
        ("--color-success", "--color-surface", 4.5),
        ("--color-warning", "--color-surface", 4.5),
        ("--color-danger", "--color-surface", 4.5),
        ("--color-series-1", "--color-surface", 3.0),
        ("--color-series-2", "--color-surface", 3.0),
        ("--color-series-2", "--color-series-1", 3.0),
        ("--color-series-3", "--color-surface", 3.0),
        // Distances the page states, which are reasons and not floors.
        ("--color-accent", "--color-success", 0),
        ("--color-teal-deep", "--color-success", 0),
        ("--color-gold", "--color-surface", 0),
        ("--color-gold-light", "--color-surface", 0),
    ];

    [Fact]
    public void Every_contrast_figure_on_the_style_page_is_the_figure_the_tokens_give_and_clears_its_bar()
    {
        var tokens = Tokens();
        string page = StylePage();
        var wrong = new List<string>();
        var computed = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string above, string below, double floor) in Pairings)
        {
            double ratio = Contrast(tokens[above], tokens[below]);
            computed.Add(Figure(ratio));
            if (ratio < floor)
            {
                wrong.Add(
                    $"{above} on {below} reads {Figure(ratio)} and the pairing needs {floor.ToString("0.0", CultureInfo.InvariantCulture)}: "
                    + "deepen the token until it clears, and record the new figure in ADR-016 and on the style page");
            }
        }

        // The worst case behind a word: the watermark's darkest ink, the accent, at the watermark's
        // strength on the page ground, seen through a glass panel, with the faintest text colour on it.
        string sheet = TokenSheet();
        double strength = double.Parse(
            Regex.Match(sheet, @"--watermark-opacity:\s*([0-9.]+)\s*;").Groups[1].Value, CultureInfo.InvariantCulture);
        double glass = double.Parse(
            Regex.Match(sheet, @"--glass-bg:\s*rgba\(255, 255, 255, ([0-9.]+)\)").Groups[1].Value, CultureInfo.InvariantCulture);
        string stroke = Over(tokens["--color-accent"], tokens["--color-bg"], strength);
        string throughPanel = Over("#ffffff", stroke, glass);
        double weakest = Contrast(tokens["--color-text-faint"], throughPanel);
        computed.Add(Figure(weakest));
        if (weakest < 4.5)
        {
            wrong.Add(
                $"the faint text colour reads {Figure(weakest)} through a panel over the watermark at its worst and needs 4.5: "
                + "lower --watermark-opacity or raise the white in --glass-bg");
        }

        // And the other way: a figure on the page that no pairing here gives is a figure nobody checks.
        foreach (Match match in Regex.Matches(page, @"(?<![\d.])\d{1,2}\.\d{2}(?!\d)"))
        {
            if (!computed.Contains(match.Value))
            {
                wrong.Add(
                    $"docs/COLOR-STYLE.md states {match.Value}, which no pairing in StyleRulesTests gives: "
                    + "either the figure is stale, so recompute it from the tokens, or the pairing is new, so add it to Pairings");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong.Distinct()));
    }
    // #endregion rule three

    // #region rule four
    [Fact]
    public void No_chart_series_is_a_status_colour_and_a_line_takes_a_status_tone_only_for_server_errors()
    {
        var tokens = Tokens();
        var wrong = new List<string>();
        var status = tokens
            .Where(entry => Regex.IsMatch(entry.Key, @"^--color-(success|warning|danger|live)$"))
            .ToList();
        Assert.True(status.Count == 4, "the sheet should carry the four status colours");

        foreach (var series in tokens.Where(entry => entry.Key.StartsWith("--color-series-", StringComparison.Ordinal)))
        {
            foreach (var state in status.Where(state => state.Value == series.Value))
            {
                wrong.Add(
                    $"{series.Key} has the value of {state.Key}: a series is an identity and a status colour is a state, "
                    + "and a series drawn in one reads as an alarm. Give the series a colour that means nothing else");
            }
        }

        // The charts' lines are toned in one component. 'bad' is the one status tone a line may take,
        // and only the server errors series may take it.
        string panel = WithoutComments(
            File.ReadAllText(Path.Combine(Root, "src", "components", "AdminPanel.tsx")));
        foreach (Match tones in Regex.Matches(panel, @"tones=\{\[([^\]]*)\]\}"))
        {
            string[] each = tones.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (string tone in each.Where(tone => tone is "'good'" or "'warn'"))
            {
                wrong.Add($"a chart passes the tone {tone}: there is no good line and no warning line, only series and server errors");
            }
        }

        int bad = Regex.Matches(panel, @"'bad'\s*,|\[\s*'bad'").Count;
        int serverErrors = Regex.Matches(panel, @"series\('5xx'").Count;
        Assert.True(serverErrors >= 1, "the traffic card's server errors series should be where this test looks for it");
        if (Regex.Matches(panel, @"tones=\{\[[^\]]*'bad'[^\]]*\]\}").Count > serverErrors)
        {
            wrong.Add(
                $"{bad} lines take the danger tone and only {serverErrors} of them is server errors: "
                + "the danger colour on a line is for a server error and nothing else");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule four

    // #region rule five
    /// <summary>Where gold is trim, by file, with what it trims there.</summary>
    private static readonly Dictionary<string, string> GoldAllowed = new(StringComparer.Ordinal)
    {
        ["src/App.module.css"] = "the rule under the phone's header, the site name on it, its focus rings, a page title's underline, the ring round Load more",
        ["src/components/SideNav.module.css"] = "the rule under the rail's brand block, the site name on it, its focus rings",
        ["src/components/AccountPanel.module.css"] = "the Account title's underline, the same trim as the Admin title's",
        ["src/components/AdminPanel.module.css"] = "the Admin title's underline, the ring round the chosen window button, the tick on a section's rule",
        ["src/components/DocsMenu.module.css"] = "the Author page: the tick on a panel's rule, the title's underline, the ring round the first button, the top edge of every other headed block",
        ["src/components/Watermark.module.css"] = "the lightning mark in the watermark, at a tenth of its strength",
    };

    [Fact]
    public void Gold_is_used_only_by_the_header_the_brand_mark_and_the_named_trim()
    {
        var wrong = new List<string>();
        foreach (string path in StyledSource())
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            string source = WithoutComments(File.ReadAllText(path));
            if (!Regex.IsMatch(source, @"--color-gold(-light)?\b") || GoldAllowed.ContainsKey(relative))
            {
                continue;
            }

            wrong.Add(
                $"{relative} uses a gold token. Gold is trim: about 2 to 1 on white, so it is never text and never data, "
                + "and beside the amber warning it reads as a warning. If this is trim, add the file to GoldAllowed with what it trims");
        }

        // And where it is allowed, it is never the colour of words on a light ground or the stroke of a chart's line.
        string panel = WithoutComments(
            File.ReadAllText(Path.Combine(Root, "src", "components", "AdminPanel.module.css")));
        foreach (Match rule in Regex.Matches(panel, @"\.(\w*Line|tile\w*|ring\w*)\s*\{[^}]*--color-gold[^}]*\}"))
        {
            wrong.Add($"the rule .{rule.Groups[1].Value} uses gold: gold is never a chart's line, a tile's top or a ring");
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule five

    // #region rule six
    [Fact]
    public void The_header_gradient_is_defined_once_and_every_header_bar_uses_it()
    {
        string sheet = WithoutComments(TokenSheet());
        Assert.True(
            Regex.Matches(sheet, @"--gradient-header\s*:").Count == 1,
            "--gradient-header should be defined exactly once, in src/styles/tokens.css");

        var wrong = new List<string>();
        foreach (string path in StyledSource().Where(path => path.EndsWith(".css", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            string source = WithoutComments(File.ReadAllText(path));
            if (Regex.IsMatch(source, @"--gradient-header\s*:"))
            {
                wrong.Add($"{relative} defines --gradient-header: there is one gradient and it lives in the token sheet");
            }

            foreach (Match rule in Regex.Matches(source, @"\{[^}]*background:\s*var\(--color-header\)[^}]*\}"))
            {
                if (!rule.Value.Contains("var(--gradient-header)", StringComparison.Ordinal))
                {
                    wrong.Add(
                        $"{relative} paints a header bar with the flat header colour and no gradient: "
                        + "add background-image: var(--gradient-header) beside it");
                }
            }
        }

        string[] bars = ["src/App.module.css", "src/components/SideNav.module.css"];
        foreach (string bar in bars)
        {
            string source = File.ReadAllText(Path.Combine(Root, bar.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.Contains("var(--gradient-header)", StringComparison.Ordinal))
            {
                wrong.Add($"{bar} holds a header bar and does not use var(--gradient-header)");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule six

    // #region rule seven
    [Fact]
    public void The_rules_held_elsewhere_are_still_held_there()
    {
        string glass = File.ReadAllText(Path.Combine(Root, "tests", "e2e", "glass.spec.ts"));
        Assert.True(
            Regex.Matches(glass, @"fadedContent\(page\)\)\.toEqual\(\[\]\)").Count >= 2,
            "tests/e2e/glass.spec.ts should hold that nothing which is read is faded, on the inventory and on the Admin tab");
        Assert.True(
            Regex.Matches(glass, @"quietWordsOnTheBareGround\(page\)\)\.toEqual\(\[\]\)").Count >= 3,
            "tests/e2e/glass.spec.ts should hold that a quiet word is never on the bare ground, on the inventory, a vehicle's page and the Admin tab");

        string measured = File.ReadAllText(Path.Combine(Root, "src", "styles", "tokens.test.ts"));
        Assert.True(
            measured.Contains("region teal-and-gold", StringComparison.Ordinal)
                && measured.Contains("region glass", StringComparison.Ordinal),
            "src/styles/tokens.test.ts should still measure the teal and gold pairings and the watermark at its worst");
    }
    // #endregion rule seven
}
