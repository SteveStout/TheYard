using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// CodeRabbit's configuration, <c>.coderabbit.yaml</c>, held to its records (ADR-129, CodeRabbit, one rung at a time).
///
/// <para>The review bot is told rules, and a rule nobody decided is a rule nobody can defend when it turns a ship
/// red. So every path instruction ends with the record that decided it, and the record has to exist; the level the
/// configuration runs at has to be the one ADR-129 names as current, so a level never moves without its addendum;
/// and the files nobody can act on stay out of the review.</para>
/// </summary>
public sealed class CodeRabbitRuleTests
{
    private static string Config() =>
        File.ReadAllText(Path.Combine(Repo.Root(), ".coderabbit.yaml")).ReplaceLineEndings("\n");

    private static string Ladder()
    {
        string folder = Path.Combine(Repo.Root(), "docs", "decisions");
        string[] found = Directory.GetFiles(folder, "ADR-129-*.md");
        Assert.True(found.Length == 1, "docs/decisions should hold exactly one ADR-129, the ladder");
        return File.ReadAllText(found[0]).ReplaceLineEndings("\n");
    }

    /// <summary>Each path instruction's text, folded onto one line, keyed by its path.</summary>
    private static List<(string Path, string Text)> Instructions()
    {
        string config = Config();
        int start = config.IndexOf("  path_instructions:\n", StringComparison.Ordinal);
        int end = config.IndexOf("  # #endregion path-instructions", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, ".coderabbit.yaml should hold path_instructions inside its region");
        var found = new List<(string, string)>();
        foreach (string entry in config[start..end].Split("\n    - path: ").Skip(1))
        {
            string[] lines = entry.Split('\n');
            string path = lines[0].Trim().Trim('"');
            string text = string.Join(' ', lines.Skip(1).Where(l => !l.TrimStart().StartsWith("instructions:", StringComparison.Ordinal)).Select(l => l.Trim())).Trim();
            found.Add((path, text));
        }
        return found;
    }

    /// <summary>The value one key holds in the level's region of the configuration.</summary>
    private static string Setting(string key)
    {
        string config = Config();
        int start = config.IndexOf("# #region the-level", StringComparison.Ordinal);
        int end = config.IndexOf("# #endregion the-level", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, ".coderabbit.yaml should hold the level inside its region");
        Match m = Regex.Match(config[start..end], $@"\n\s*{Regex.Escape(key)}: (\S+)\n");
        Assert.True(m.Success, $".coderabbit.yaml should set {key} in the level's region");
        return m.Groups[1].Value;
    }

    // #region every-rule-has-a-record
    [Fact]
    public void Every_path_instruction_ends_with_a_record_that_exists()
    {
        var instructions = Instructions();
        Assert.NotEmpty(instructions);
        string folder = Path.Combine(Repo.Root(), "docs", "decisions");
        foreach ((string path, string text) in instructions)
        {
            Match cite = Regex.Match(text, @"\(ADR-(\d{3})\)\.?$");
            Assert.True(cite.Success, $"the instruction for {path} should end with the record that decided it, as (ADR-NNN): {text}");
            string number = cite.Groups[1].Value;
            Assert.True(Directory.GetFiles(folder, $"ADR-{number}-*.md").Length == 1, $"the instruction for {path} cites ADR-{number}, which docs/decisions does not hold");
        }
    }

    [Fact]
    public void Every_rule_the_ladder_lists_is_one_the_configuration_tells_and_the_other_way_round()
    {
        string ladder = Ladder();
        int start = ladder.IndexOf("### The rules at Level 0", StringComparison.Ordinal);
        Assert.True(start > 0, "ADR-129 should list the rules CodeRabbit is told");
        var listed = Regex.Matches(ladder[start..], @"^\| [^|]+ \| (ADR-\d{3}) \| \w+ \|$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Order().ToList();
        var told = Instructions().Select(i => Regex.Match(i.Text, @"\((ADR-\d{3})\)\.?$").Groups[1].Value).Order().ToList();
        Assert.Equal(listed, told);
    }
    // #endregion every-rule-has-a-record

    // #region the-level-is-the-records
    [Fact]
    public void The_configuration_runs_at_the_level_the_ladder_names_as_current()
    {
        string ladder = Ladder();
        Match current = Regex.Match(ladder, @"^Current level: (\d)$", RegexOptions.Multiline);
        Assert.True(current.Success, "ADR-129 should name its current level on a line of its own, \"Current level: N\"");
        Match row = Regex.Match(ladder, $@"^\| {current.Groups[1].Value} \| (\w+) \| (true|false) \| (true|false) \|", RegexOptions.Multiline);
        Assert.True(row.Success, $"ADR-129's ladder should have a row for level {current.Groups[1].Value}");
        Assert.Equal(row.Groups[1].Value, Setting("profile"));
        Assert.Equal(row.Groups[2].Value, Setting("enabled"));
        Assert.Equal(row.Groups[3].Value, Setting("fail_commit_status"));
    }

    [Fact]
    public void The_ladder_has_four_levels_and_only_the_last_is_assertive()
    {
        string ladder = Ladder();
        var rows = Regex.Matches(ladder, @"^\| (\d) \| (\w+) \| (true|false) \| (true|false) \|", RegexOptions.Multiline);
        Assert.Equal(["0", "1", "2", "3"], rows.Select(r => r.Groups[1].Value));
        Assert.Equal(["chill", "chill", "chill", "assertive"], rows.Select(r => r.Groups[2].Value));
        Assert.Equal("false", rows[0].Groups[3].Value);
        Assert.Equal("false", rows[0].Groups[4].Value);
    }
    // #endregion the-level-is-the-records

    // #region the-log-counts-labels
    /// <summary>
    /// The findings log counts a finding by the label in its header and nothing else, so a Minor finding whose text
    /// says "major" is still one Minor, and a remark with no label is a nitpick. Only the gated commit's findings
    /// count, and a reply in a finding's thread is not another finding.
    /// </summary>
    [Fact]
    public void The_findings_log_counts_each_finding_by_its_header_label_alone()
    {
        // The script runs on GitHub's Ubuntu runners and on Linux; a Windows checkout may have no python3.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        object[] comments =
        [
            new { body = "<!-- coderabbit-ai-generated:v1 -->\n\n**<picture><img src=\"x/amber-16.svg\" alt=\"\"></picture> Minor** · **Functional Correctness**\n\nNot a _Major_ problem." },
            new { body = "**<picture><img src=\"x/major-16.svg\" alt=\"\"></picture> Major** · **Data Integrity**\n\nA minor detail too." },
            new { body = "_Potential issue_ | _Critical_\n\nThe older header." },
            new { body = "A remark with no label that mentions a trivial thing." },
            new { body = "**<picture></picture> Major** · a reply in the thread is not a finding", in_reply_to_id = 7, commit_id = "abc" },
            new { body = "**<picture></picture> Critical** · on an earlier commit of the same request", commit_id = "old" },
        ];
        string file = Path.Combine(Path.GetTempPath(), $"coderabbit-comments-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(comments));
        try
        {
            var start = new ProcessStartInfo("python3", $"scripts/coderabbit_review.py count \"{file}\" abc") { WorkingDirectory = Repo.Root(), RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, errors);
            using var counted = JsonDocument.Parse(output);
            JsonElement findings = counted.RootElement.GetProperty("findings");
            Assert.Equal(1, findings.GetProperty("critical").GetInt32());
            Assert.Equal(1, findings.GetProperty("major").GetInt32());
            Assert.Equal(1, findings.GetProperty("minor").GetInt32());
            Assert.Equal(0, findings.GetProperty("trivial").GetInt32());
            Assert.Equal(1, findings.GetProperty("nitpick").GetInt32());
        }
        finally
        {
            File.Delete(file);
        }
    }
    // #endregion the-log-counts-labels

    // #region every-ship-has-one-row
    /// <summary>
    /// Every ship has exactly one row: a read that wrote none still gets one that says so, and a re-run of a version
    /// replaces its row rather than adding a second.
    /// </summary>
    [Fact]
    public void Every_ship_has_one_row_in_the_findings_log_whatever_the_read_did()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string dir = Path.Combine(Path.GetTempPath(), $"coderabbit-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string row = Path.Combine(dir, "row.json");
            string log = Path.Combine(dir, "log.json");
            Python(dir, $"missing 1.0.4.900 0123456789abcdef \"{row}\"");
            Python(dir, $"fold \"{row}\" \"{log}\"");
            Python(dir, $"fold \"{row}\" \"{log}\"");
            using var read = JsonDocument.Parse(File.ReadAllText(log));
            JsonElement rows = read.RootElement.GetProperty("rows");
            Assert.Equal(1, rows.GetArrayLength());
            Assert.Equal("1.0.4.900", rows[0].GetProperty("version").GetString());
            Assert.Equal("01234567", rows[0].GetProperty("sha").GetString());
            Assert.Equal("not reviewed, the read wrote no row", rows[0].GetProperty("reviewed").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Python(string dir, string args)
    {
        var start = new ProcessStartInfo("python3", $"\"{Path.Combine(Repo.Root(), "scripts", "coderabbit_review.py")}\" {args}") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        string errors = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
    }
    // #endregion every-ship-has-one-row

    [Theory]
    [InlineData("!api/TheYard.Migrations.Sqlite/**/*.cs")]
    [InlineData("!data/test-results.json")]
    [InlineData("!data/coderabbit-findings.json")]
    [InlineData("!**/package-lock.json")]
    [InlineData("!**/*.png")]
    [InlineData("!**/*.jpg")]
    [InlineData("!**/*.webp")]
    public void Generated_and_binary_files_stay_out_of_the_review(string filter)
    {
        Assert.Contains($"    - \"{filter}\"\n", Config(), StringComparison.Ordinal);
    }
}
