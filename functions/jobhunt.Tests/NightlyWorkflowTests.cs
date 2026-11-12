using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// nightly.yml's plumbing around the lane (ADR-109): a lane whose setup died before it began runs once more and a lane
/// that ran is never run twice, and the usage and run reports read whichever attempt ran.
/// </summary>
public sealed class NightlyWorkflowTests
{
    private static readonly string Yaml = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "nightly.yml"));

    [Fact]
    public void A_lane_whose_setup_died_runs_once_more_with_the_same_inputs()
    {
        string first = Step("The lane");
        string again = Step("The lane, once more");

        Assert.Contains("continue-on-error: true", first, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", again, StringComparison.Ordinal);
        Assert.Contains("id: lane_again", again, StringComparison.Ordinal);
        Assert.Contains("if: steps.began.outputs.again == 'true'", again, StringComparison.Ordinal);
        Assert.Equal(Body(first), Body(again));
        Assert.True(Yaml.IndexOf("- name: The lane, once more", StringComparison.Ordinal) > Yaml.IndexOf("- name: Did the lane begin", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_a_failed_step_with_no_execution_file_and_nothing_written_runs_again()
    {
        string began = Step("Did the lane begin");

        Assert.Contains("if: steps.lane.outcome == 'failure'", began, StringComparison.Ordinal);
        Assert.Contains("EXEC: ${{ steps.lane.outputs.execution_file }}", began, StringComparison.Ordinal);
        Assert.Contains("[ -z \"$EXEC\" ] || [ ! -f \"$EXEC\" ]", began, StringComparison.Ordinal);
        Assert.Contains("ls -A \"$JOBHUNT/$RUN_DIR\"", began, StringComparison.Ordinal);
    }

    [Fact]
    public void The_verdict_is_the_attempt_that_ran_and_the_reports_read_it()
    {
        string verdict = Step("The lane's verdict");

        Assert.Contains("FIRST: ${{ steps.lane.outcome }}", verdict, StringComparison.Ordinal);
        Assert.Contains("SECOND: ${{ steps.lane_again.outcome }}", verdict, StringComparison.Ordinal);
        Assert.Contains("[ \"$came\" = \"success\" ]", verdict, StringComparison.Ordinal);
        Assert.Contains("EXEC: ${{ steps.lane_again.outputs.execution_file || steps.lane.outputs.execution_file }}", Step("Tokens and wall time"), StringComparison.Ordinal);
        Assert.True(Yaml.IndexOf("- name: The lane's verdict", StringComparison.Ordinal) < Yaml.IndexOf("- name: Report this run to the site", StringComparison.Ordinal));
    }

    /// <summary>One step's text, from its name to the next step or region marker at the same depth.</summary>
    internal static string Step(string name)
    {
        string head = $"      - name: {name}\n";
        int start = Yaml.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no step named {name}");
        int end = Ends(start + head.Length);
        return Yaml[start..end];
    }

    /// <summary>A step's run script, its indentation taken off.</summary>
    internal static string Script(string name)
    {
        string[] lines = Step(name).Split('\n');
        int at = Array.FindIndex(lines, l => l.TrimEnd() == "        run: |");
        Assert.True(at >= 0, $"step {name} has no run block");
        return string.Join('\n', lines.Skip(at + 1).TakeWhile(l => l.Length == 0 || l.StartsWith("          ", StringComparison.Ordinal)).Select(l => l.Length >= 10 ? l[10..] : string.Empty)) + "\n";
    }

    private static int Ends(int from)
    {
        int next = Yaml.Length;
        foreach (string marker in new[] { "\n      - name: ", "\n      # #region", "\n      # #endregion" })
        {
            int at = Yaml.IndexOf(marker, from, StringComparison.Ordinal);
            if (at >= 0 && at < next) { next = at + 1; }
        }
        return next;
    }

    /// <summary>A lane step's inputs: everything from its timeout on, the env and the with blocks.</summary>
    private static string Body(string step) => step[step.IndexOf("        timeout-minutes:", StringComparison.Ordinal)..].Replace("continue-on-error: true\n", "", StringComparison.Ordinal);

    internal static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".github", "workflows")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException(".github/workflows not found above " + AppContext.BaseDirectory);
    }
}
