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
        ("--color-who-people", "--color-surface", 3.0),
        ("--color-who-scanners", "--color-surface", 3.0),
        ("--color-who-self", "--color-surface", 3.0),
        // The Mark VII marks (the tweaks pass, 25 September): graphics, so 3.0.
        ("--color-mark-teal", "--color-surface", 3.0),
        ("--color-mark-gold", "--color-surface", 3.0),
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

        // The charts' lines are toned in the Admin tab and its cards, one file each since the
        // workbench. 'bad' is the one status tone a line may take, and only the server errors
        // series may take it.
        string panel = WithoutComments(string.Join(
            "\n",
            Directory.EnumerateFiles(Path.Combine(Root, "src", "components", "admin"), "*.tsx")
                .Prepend(Path.Combine(Root, "src", "components", "AdminPanel.tsx"))
                .Select(File.ReadAllText)));
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
        ["src/components/Landing.module.css"] = "the landing title's underline, the ring round each tile's icon",
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

    // #region rule nine, the one face
    /// <summary>
    /// One face for the whole site (Steve, 2026-09-24: IBM Plex Sans, "the best
    /// for phones and tablets"). The token names it first; no sheet or component
    /// names Poppins, the face before it, outside a comment; and a monospaced
    /// face is only ever the code token, on code: a reading is the one face
    /// with tabular figures, which the body sets once.
    /// </summary>
    [Fact]
    public void The_one_face_is_IBM_Plex_Sans_and_monospace_is_only_code()
    {
        var wrong = new List<string>();
        string sheet = WithoutComments(TokenSheet());
        if (!Regex.IsMatch(sheet, @"--font-sans:\s*'IBM Plex Sans',"))
        {
            wrong.Add("src/styles/tokens.css should name 'IBM Plex Sans' first in --font-sans");
        }
        if (!Regex.IsMatch(sheet, @"body\s*\{[^}]*font-variant-numeric:\s*tabular-nums;"))
        {
            wrong.Add("src/styles/tokens.css should set tabular figures on body, once, for every reading");
        }

        foreach (string path in StyledSource().Append(Path.Combine(Root, "src", "styles", "tokens.css")))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            string source = WithoutComments(File.ReadAllText(path));
            if (source.Contains("Poppins", StringComparison.Ordinal))
            {
                wrong.Add($"{relative} names Poppins: the face is IBM Plex Sans, through var(--font-sans)");
            }
            if (!relative.EndsWith("tokens.css", StringComparison.Ordinal)
                && Regex.IsMatch(source, @"font-family:[^;]*monospace"))
            {
                wrong.Add($"{relative} writes a monospaced face of its own: code takes var(--font-code), and a reading takes the one face");
            }
        }

        // The code token is for code: a selector that takes it names code, a pre or a stack.
        foreach (string path in StyledSource().Where(path => path.EndsWith(".css", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            foreach (Match rule in Regex.Matches(WithoutComments(File.ReadAllText(path)), @"([^{}]+)\{[^}]*var\(--font-code\)[^}]*\}"))
            {
                string selector = rule.Groups[1].Value.Trim();
                if (!Regex.IsMatch(selector, @"code|pre|\.sql|\.detail"))
                {
                    wrong.Add($"{relative} gives the code face to '{selector}', which is not code");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule nine, the one face

    // #region rule nine, the operator's look
    /// <summary>
    /// The operator's look is one shared sheet, src/styles/operator.css, drawn
    /// from the tokens (ADR: The glass look, the addendum on the operator's
    /// look): a panel, a card or a tile is .op-glass, so no component sheet
    /// gives one its own ground, border, rule or radius, spaces its capitals
    /// by a number of its own, or rounds a button to anything but a pill. The
    /// views still to come onto it are listed with the version that brings
    /// them, and the list is empty when the lane that started it closes.
    /// </summary>
    private static readonly Dictionary<string, string> NotYetOnTheLook = new(StringComparer.Ordinal)
    {
    };

    /// <summary>The header and the site's own rail are the frame, not panels on it (Steve: "leave the header and page background the same").</summary>
    private static readonly string[] TheFrame = ["src/App.module.css", "src/components/SideNav.module.css"];

    /// <summary>A name the scan reads as a panel's that is not one, each with why.</summary>
    private static readonly Dictionary<string, string> NotAPanel = new(StringComparer.Ordinal)
    {
        ["src/components/StoreBar.module.css .bar"] = "the store band under the header is part of the frame, as the header is",
        ["src/components/AdminPanel.module.css .scannerStrip"] = "the activity card's quiet line of what scanners probed, on the page ground inside the card: a line in a card, not a panel",
    };

    [Fact]
    public void Every_panel_is_the_shared_glass_and_no_sheet_draws_its_own_rule_bracket_or_tracking()
    {
        var wrong = new List<string>();
        var panel = new Regex(@"\.[A-Za-z]*(?:[Pp]anel|[Cc]ard|[Tt]ile|[Hh]ero|[Pp]roofItem|[Ss]trip|[Dd]ialog|\bbar)\b(?![-\w])", RegexOptions.Compiled);
        var own = new Regex(@"(?:^|;)\s*(background|border(?:-top)?|border-radius|box-shadow)\s*:", RegexOptions.Compiled);
        int sheets = 0;

        foreach (string path in StyledSource().Where(path => path.EndsWith(".module.css", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            if (TheFrame.Contains(relative))
            {
                continue;
            }
            sheets++;
            string css = WithoutComments(File.ReadAllText(path));
            foreach (Match rule in Regex.Matches(css, @"([^{}@]+)\{([^{}]*)\}"))
            {
                string selector = rule.Groups[1].Value.Trim();
                string body = rule.Groups[2].Value;
                // The last compound of each selector in the list: a panel's own ground, not a thing inside it.
                foreach (string one in selector.Split(','))
                {
                    string last = one.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                    Match named = panel.Match(last);
                    bool state = last.Contains(':', StringComparison.Ordinal) && !last.Contains(":global", StringComparison.Ordinal);
                    if (!named.Success || state || last.Contains("::", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string key = $"{relative} {named.Value}";
                    Match drawn = own.Match(body);
                    if (drawn.Success && !NotYetOnTheLook.ContainsKey(key) && !NotAPanel.ContainsKey(key))
                    {
                        wrong.Add($"{relative} '{one.Trim()}' sets its own {drawn.Groups[1].Value}: a panel, a card or a tile is .op-glass (src/styles/operator.css), which owns the ground, the rule, the brackets and the radius");
                    }
                }
                foreach (Match tracking in Regex.Matches(body, @"letter-spacing:\s*([^;]+);"))
                {
                    string value = tracking.Groups[1].Value.Trim();
                    if (value is "var(--readout-tracking)" or "0" or "normal" or "inherit")
                    {
                        continue;
                    }
                    if (!NotYetOnTheLook.ContainsKey($"{relative} letter-spacing"))
                    {
                        wrong.Add($"{relative} '{selector}' spaces its letters by {value}: spaced capitals take var(--readout-tracking)");
                    }
                }
                if (Regex.IsMatch(selector, @"(^|[\s>+~,])button\b") && Regex.Match(body, @"border-radius:\s*([^;]+);") is { Success: true } radius
                    && radius.Groups[1].Value.Trim() is not ("var(--radius-full)" or "50%"))
                {
                    wrong.Add($"{relative} '{selector}' rounds a button to {radius.Groups[1].Value.Trim()}: every button is a pill, var(--radius-full), or a circle");
                }
            }
        }

        Assert.True(sheets > 15, $"only {sheets} component sheets were read, so this scan is reading the wrong folder");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion rule nine, the operator's look

    // #region rule eight
    /// <summary>
    /// One consistent experience, from tokens (1.0.3.9). The sweep of every
    /// view at three widths in two engines found three focus rings, eight
    /// button heights for the same kinds of control, two input borders and
    /// radii, and one heading at the wrong weight. Each is a token now, and
    /// this holds every sheet to them: an outline is one of the three ring
    /// tokens and is followed by an offset token; every control named below
    /// takes its height from a height token; every page title takes its weight
    /// from the title token.
    /// </summary>
    [Fact]
    public void Every_focus_ring_control_height_and_title_weight_comes_from_the_token_sheet()
    {
        var wrong = new List<string>();
        var ring = new Regex(@"outline:\s*([^;]+);", RegexOptions.Compiled);
        var offset = new Regex(@"outline-offset:\s*([^;]+);", RegexOptions.Compiled);
        int rings = 0;

        foreach (string path in StyledSource().Where(path => path.EndsWith(".css", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            string css = WithoutComments(File.ReadAllText(path));
            foreach (Match match in ring.Matches(css))
            {
                string value = match.Groups[1].Value.Trim();
                if (value == "none" || value == "0")
                {
                    continue;
                }
                rings++;
                if (value is not ("var(--focus-ring)" or "var(--focus-ring-on-dark)" or "var(--focus-ring-warning)"))
                {
                    wrong.Add($"{relative} draws a focus ring as '{value}': use var(--focus-ring), var(--focus-ring-on-dark) or var(--focus-ring-warning)");
                    continue;
                }
                string after = css[(match.Index + match.Length)..];
                var next = offset.Match(after);
                if (!next.Success || next.Index > 80)
                {
                    wrong.Add($"{relative} draws a focus ring with no outline-offset token after it");
                }
                else if (next.Groups[1].Value.Trim() is not ("var(--focus-ring-offset)" or "var(--focus-ring-inset)"))
                {
                    wrong.Add($"{relative} sets an outline-offset of '{next.Groups[1].Value.Trim()}': use var(--focus-ring-offset) or var(--focus-ring-inset)");
                }
            }
        }
        Assert.True(rings > 25, $"only {rings} focus rings were read, so this scan is reading the wrong files");

        // The controls, by sheet and selector, and the height token each takes.
        var controls = new (string Sheet, string Selector, string Token)[]
        {
            ("src/components/AdminPanel.module.css", ".back", "--pill-height"),
            ("src/components/AccountPanel.module.css", ".back", "--pill-height"),
            ("src/components/VehicleDetail.module.css", ".back", "--pill-height"),
            ("src/components/DocsMenu.module.css", ".copyLink", "--pill-height"),
            ("src/components/DocsMenu.module.css", ".close", "--pill-height"),
            ("src/components/AccountPanel.module.css", ".input", "--control-height"),
            ("src/components/AccountPanel.module.css", ".primary,\n.secondary", "--control-height"),
            ("src/components/BidPanel.module.css", ".bidButton", "--control-height"),
            ("src/components/FilterBar.module.css", ".searchInput", "--control-height"),
            ("src/components/FilterBar.module.css", ".select", "--control-height"),
            ("src/components/DocsMenu.module.css", ".prose :global(.author-button)", "--control-height-lg"),
        };
        foreach (var (sheet, selector, token) in controls)
        {
            string block = RuleBlock(sheet, selector);
            if (!block.Contains($"min-height: var({token});", StringComparison.Ordinal))
            {
                wrong.Add($"{sheet} {selector.Replace("\n", " ")} should take its height from var({token})");
            }
        }
        var titles = new (string Sheet, string Selector)[]
        {
            ("src/components/Landing.module.css", ".heading"),
            ("src/components/AdminPanel.module.css", ".title"),
            ("src/components/AccountPanel.module.css", ".title"),
            ("src/components/VehicleDetail.module.css", ".title"),
        };
        foreach (var (sheet, selector) in titles)
        {
            if (!RuleBlock(sheet, selector).Contains("font-weight: var(--title-weight);", StringComparison.Ordinal))
            {
                wrong.Add($"{sheet} {selector} should take its weight from var(--title-weight)");
            }
        }
        // A pixel height on a control belongs in the sheet, not in a component's module.
        foreach (string path in StyledSource().Where(path => path.EndsWith(".module.css", StringComparison.Ordinal)))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            foreach (Match match in Regex.Matches(WithoutComments(File.ReadAllText(path)), @"min-height:\s*(34|40|44|48)px;"))
            {
                wrong.Add($"{relative} writes 'min-height: {match.Groups[1].Value}px' where a height token belongs");
            }
        }

        // And the operator's look (the workbench, 2026-09-24): the rule, the brackets, the readout and the ring.
        foreach (string token in new[] { "--pill-height", "--control-height", "--control-height-lg", "--focus-ring", "--focus-ring-on-dark", "--focus-ring-offset", "--focus-ring-inset", "--title-weight", "--badge-weight", "--input-border", "--input-radius", "--icon-sm", "--icon-md", "--icon-stroke", "--rule-panel", "--rule-tile", "--bracket-size", "--bracket-stroke", "--readout-size", "--readout-tracking", "--ring-track", "--ring-first", "--ring-second" })
        {
            if (!TokenSheet().Contains(token + ":", StringComparison.Ordinal))
            {
                wrong.Add($"src/styles/tokens.css should define {token}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>The declarations of one rule, from its selector to the closing brace, without comments.</summary>
    private static string RuleBlock(string sheet, string selector)
    {
        string css = WithoutComments(File.ReadAllText(Path.Combine(Root, sheet.Replace('/', Path.DirectorySeparatorChar))));
        int at = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{sheet} has no rule '{selector.Replace("\n", " ")}'");
        int end = css.IndexOf('}', at);
        return css[at..end];
    }
    // #endregion rule eight
}
