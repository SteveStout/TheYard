using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// An empty run is a failed run, for the model lanes too: check_empty_runs.py writes dispatch/EMPTY-RUNS.md each morning
/// before the card, and the card prints each of its "EMPTY RUN:" lines at the top, every day, not only on Mondays.
/// </summary>
public sealed class TodayEmptyRunsTests
{
    private static TodayInputs Inputs(string emptyRuns) =>
        new(new DateTime(2026, 11, 13, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            "", "", "", "", "", null, "", "", [], "Funnel, last 7 days: 1 call booked.")
        {
            Signal = "Hiring signal: 101 companies.",
            EmptyRuns = emptyRuns,
        };

    [Fact]
    public void Each_empty_run_line_prints_at_the_top_under_the_hiring_signal()
    {
        const string File = "> RULES FIRST: read systemRules.md.\n\n# EMPTY RUNS, written by check_empty_runs.py 2026-11-13T13:10:04Z\n\nEMPTY RUN: server6am invites, 2 runs, since 11/11\n";

        string card = TodayCard.Render(Inputs(File));

        int line = card.IndexOf("**EMPTY RUN: server6am invites, 2 runs, since 11/11**", StringComparison.Ordinal);
        Assert.True(line > card.IndexOf("Hiring signal: 101 companies.", StringComparison.Ordinal), card);
        Assert.True(line < card.IndexOf("## ", StringComparison.Ordinal), card);
        Assert.DoesNotContain("RULES FIRST: read systemRules.md.", card[..line], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("> RULES FIRST: read systemRules.md.\n\n# EMPTY RUNS, written by check_empty_runs.py 2026-11-13T13:10:04Z\n\nnone\n")]
    public void No_empty_run_prints_nothing(string file) => Assert.DoesNotContain("EMPTY RUN", TodayCard.Render(Inputs(file)), StringComparison.Ordinal);

    [Fact]
    public void The_card_reads_the_file_check_empty_runs_writes() => Assert.Equal("dispatch/EMPTY-RUNS.md", TodayCard.EmptyRunsPath);
}
