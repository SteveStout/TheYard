using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The quality ratchet: the numbers in <c>.github/quality-ratchet.json</c>, the record that
/// says what each one counts, and the two workflows that hold them (ADR: The numbers that may
/// only fall).
///
/// <para>The ratchet is a file of counts with no meaning written beside them. A reader who
/// finds a run red on <c>clock_outside_composition</c> needs to know what that counts and why
/// it is held before they can do anything about it, and a number nobody can explain is a
/// number the next person deletes. So the record carries a row per number and this reads the
/// two against each other both ways, as <c>RuleTableTests</c> reads the rules table: a key with
/// no row is a number nobody has justified, and a row with no key is a rule that stopped being
/// enforced while its explanation stayed on the page.</para>
///
/// <para>The rest holds the mechanism rather than the map. A ratchet that is explained, agreed
/// and run nowhere is worth nothing, so the scanner has to be run by CI and by the gate against
/// that file, the gate's verdict has to read its result, and the coverage floor it sits beside
/// has to still be the floor it was.</para>
/// </summary>
public class QualityRatchetTests
{
    private const string Record = "ADR-122-the-numbers-that-may-only-fall.md";

    /// <summary>The command both workflows run, spelled once so a change to either is read here.</summary>
    private const string Command = "python3 .github/quality.py . --ratchet .github/quality-ratchet.json";

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(Repo.Root(), Path.Combine(parts))).ReplaceLineEndings("\n");

    // #region both-ways
    /// <summary>Every name in the ratchet file, in the order the file writes them.</summary>
    private static List<string> KeysInTheFile()
    {
        using var file = JsonDocument.Parse(Read(".github", "quality-ratchet.json"));
        var keys = file.RootElement.EnumerateObject().Select(number => number.Name).ToList();
        Assert.True(keys.Count >= 14, $"the ratchet holds {keys.Count} numbers, which is fewer than it had");
        return keys;
    }

    /// <summary>
    /// The first cell of every row of the one table in the record whose header names the number,
    /// with the backticks taken off. The header and the dashes under it are dropped; the table
    /// ends at the first line that is not a row.
    /// </summary>
    private static List<string> KeysInTheRecord()
    {
        string[] lines = Read("docs", "decisions", Record).Split('\n');
        int header = Array.FindIndex(lines, line => line.StartsWith("| The number |", StringComparison.Ordinal));
        Assert.True(header >= 0, $"{Record} should carry the table of numbers");

        var keys = new List<string>();
        for (int i = header + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            keys.Add(lines[i].Trim('|').Split('|')[0].Trim().Trim('`'));
        }

        // A table that empties itself agrees with nothing and passes every row check there is.
        Assert.True(keys.Count >= 14, $"the table explains {keys.Count} numbers, which is fewer than it had");
        return keys;
    }

    [Fact]
    public void Every_number_the_ratchet_holds_has_a_row_in_the_record_that_says_what_it_counts()
    {
        var explained = KeysInTheRecord().ToHashSet(StringComparer.Ordinal);
        var missing = KeysInTheFile().Where(key => !explained.Contains(key)).ToList();

        Assert.True(
            missing.Count == 0,
            "the ratchet holds these numbers and the record explains none of them: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_row_in_the_record_names_a_number_the_ratchet_actually_holds()
    {
        var held = KeysInTheFile().ToHashSet(StringComparer.Ordinal);
        var stale = KeysInTheRecord().Where(key => !held.Contains(key)).ToList();

        Assert.True(
            stale.Count == 0,
            "the record explains these numbers and the ratchet holds none of them: " + string.Join(", ", stale));
    }

    /// <summary>
    /// A ceiling is a whole count and a floor is named for being one, which is the whole of the
    /// file's grammar: the scanner reads the suffix to decide which way a number may move, so a
    /// name that lies about its direction turns the step into one that can never be failed.
    /// </summary>
    [Fact]
    public void A_number_named_a_floor_is_the_only_kind_that_may_rise()
    {
        using var file = JsonDocument.Parse(Read(".github", "quality-ratchet.json"));
        var wrong = new List<string>();

        foreach (var number in file.RootElement.EnumerateObject())
        {
            bool floor = number.Name.EndsWith("higher_is_better", StringComparison.Ordinal);
            bool whole = number.Value.TryGetInt32(out int _);
            if (!floor && !whole)
            {
                wrong.Add($"{number.Name} is a ceiling and its limit is not a whole count");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        Assert.Contains(file.RootElement.EnumerateObject(), number => number.Name.EndsWith("higher_is_better", StringComparison.Ordinal));
    }
    // #endregion both-ways

    // #region run-everywhere
    /// <summary>
    /// CI runs the scanner against the file in the api job, beside the coverage step that holds
    /// the other ratchet this project has, and under <c>if: always()</c> so a reader of a red run
    /// sees both numbers rather than only the first one to fail.
    /// </summary>
    [Fact]
    public void Ci_runs_the_ratchet_in_the_api_job_beside_the_coverage_step()
    {
        string ci = Read(".github", "workflows", "ci.yml");
        Assert.Contains(Command, ci, StringComparison.Ordinal);

        int coverage = ci.IndexOf(".github/coverage.py TestResults", StringComparison.Ordinal);
        int quality = ci.IndexOf(Command, StringComparison.Ordinal);
        Assert.True(coverage > 0 && quality > coverage, "the ratchet step should sit in the api job, after the coverage step");

        string step = ci[(ci.LastIndexOf("- name:", quality, StringComparison.Ordinal))..quality];
        Assert.Contains("if: always()", step, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate runs the same command and its verdict reads the result, so the step cannot run
    /// and not count. A check that records a result nothing reads is the failure mode the gate's
    /// own workflow test exists for, and this holds this one check to the same bar.
    /// </summary>
    [Fact]
    public void The_gate_runs_the_ratchet_and_its_verdict_turns_the_run_red()
    {
        string gate = Read(".github", "workflows", "gate.yml");
        Assert.Contains(Command, gate, StringComparison.Ordinal);
        Assert.Contains("echo \"quality=$code\"", gate, StringComparison.Ordinal);

        int verdict = gate.IndexOf("- name: The verdict", StringComparison.Ordinal);
        Assert.True(verdict > 0, "gate.yml should hold a verdict");
        Assert.Contains("rest.result quality", gate[verdict..], StringComparison.Ordinal);
    }

    /// <summary>
    /// The coverage floor the ratchet sits beside is the floor it has been: 90 per cent of lines
    /// and 75 per cent of branches. It is asserted here because the two live in the same job now
    /// and the cheapest way to make a red run green is to lower the other number in the file next
    /// to the one that failed.
    /// </summary>
    [Fact]
    public void The_coverage_floor_beside_it_is_still_ninety_and_seventy_five()
    {
        string ci = Read(".github", "workflows", "ci.yml");
        Assert.Matches(new Regex(@"\.github/coverage\.py TestResults 90\.0 75\.0"), ci);
    }
    // #endregion run-everywhere
}
