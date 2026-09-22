using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The counts the landing page shows (ADR: The landing page and the site map,
/// the addendum on the recruiter's first minute). The strip is an argument
/// made with numbers, so the numbers are the gate's own file added up and
/// nothing else: a suite the gate skipped and carried forward keeps the
/// version whose gate ran it, and a file with no suites is not an invented
/// zero-tests build, it is a summary that says so.
/// </summary>
public class TestSummaryTests
{
    private const string Results = """
        {
          "version": "1.0.1.6",
          "ranAt": "2026-09-22T16:06:34.243Z",
          "gateSeconds": 312,
          "checks": [{"name":"Prettier","passed":true,"seconds":9}],
          "suites": [
            {"id":"vitest","name":"Vitest","seconds":4,"passed":224,"failed":0,"skipped":0,"tests":[["a.test.ts","one","p",1]]},
            {"id":"xunit-sqlite","name":"xUnit on SQLite","seconds":26,"passed":581,"failed":1,"skipped":2,"tests":[]},
            {"id":"xunit-cosmos","name":"xUnit on Cosmos DB","seconds":102,"passed":581,"failed":0,"skipped":0,"carried":"1.0.1.4","tests":[]}
          ]
        }
        """;

    // #region summary
    [Fact]
    public void The_summary_is_the_gate_s_own_counts_with_the_tests_left_out()
    {
        var summary = TestSummary.Parse(Results);

        Assert.Equal("1.0.1.6", summary.Version);
        Assert.Equal(312, summary.GateSeconds);
        Assert.Equal(1386, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(2, summary.Skipped);
        Assert.Equal(3, summary.Suites.Count);
        Assert.Equal(["vitest", "xunit-sqlite", "xunit-cosmos"], summary.Suites.Select(suite => suite.Id));
    }

    [Fact]
    public void A_carried_suite_says_which_version_ran_it_and_a_suite_that_ran_says_nothing()
    {
        var summary = TestSummary.Parse(Results);

        Assert.Equal("1.0.1.4", summary.Suites.Single(suite => suite.Id == "xunit-cosmos").Carried);
        Assert.Null(summary.Suites.Single(suite => suite.Id == "vitest").Carried);
    }

    [Fact]
    public void A_file_with_nothing_in_it_reads_as_nothing_rather_than_as_a_green_gate()
    {
        var summary = TestSummary.Parse("{}");

        Assert.Equal("", summary.Version);
        Assert.Equal(0, summary.GateSeconds);
        Assert.Equal(0, summary.Passed);
        Assert.Empty(summary.Suites);
    }

    [Fact]
    public void The_build_s_own_results_file_reads_and_is_the_file_the_admin_tab_reads()
    {
        string path = Path.Combine(Repo.Root(), "data", "test-results.json");
        var summary = TestSummary.Of(path);

        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", summary.Version);
        Assert.NotEmpty(summary.Suites);
        Assert.All(summary.Suites, suite => Assert.True(suite.Passed >= 0));
        // Read twice: the second read is the cached answer and the same figures.
        Assert.Equal(summary.Passed, TestSummary.Of(path).Passed);
    }
    // #endregion summary
}
