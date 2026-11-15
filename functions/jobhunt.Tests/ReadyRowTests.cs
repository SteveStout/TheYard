using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// READY rows the apply lane can submit: the remote gate read on two real postings as the board's single-job API
/// answered them (saved by the jobs lane's run of 10 November), each failing "not remote" with the words it read.
/// </summary>
public sealed class ReadyRowTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    /// <summary>A saved Greenhouse single-job answer as the scan reads it: its text, location and stance, a band that passes.</summary>
    private static Candidate Posting(string file, string company)
    {
        string json = File.ReadAllText(ReadyRowFixtures.Path(file));
        var board = Assert.Single(PublicBoardReader.Read("greenhouse", "{\"jobs\":[" + json + "]}"));
        return new Candidate("feed:greenhouse", company, board.Title, board.Url, "greenhouse", board.Posted, board.Location,
            PostingRules.Stance(board.Workplace, board.Location, board.IsRemote), 215_000, "feed", board.Text);
    }

    [Fact]
    public void NinjaOne_s_saved_posting_fails_not_remote_on_its_nineteen_states()
    {
        var verdict = JobsGates.Judge(Posting("gh_ninjaone_4469473009.json", "NinjaOne"), [], [], Today);
        Assert.Equal("not remote: remote only in named states, Missouri not among them: CA, CO, CT, FL, GA, IL, KS, MA, MD, ME, NJ, NC, NY, OH, OR, TN, TX, VA, WA", verdict.Failed);
    }

    [Fact]
    public void SingleStore_s_saved_posting_never_says_remote_and_fails_not_remote_with_its_location()
    {
        var verdict = JobsGates.Judge(Posting("gh_singlestore_8318150.json", "SingleStore"), [], [], Today);
        Assert.Equal(JobsGates.CountryOnly, verdict.Remote);
        Assert.Equal("not remote: United States, and the title and text never say remote", verdict.Failed);
        Assert.Equal(verdict.Failed, PounceRules.Gate(verdict, [], new HashSet<string>()));
    }
}

/// <summary>The jobs fixtures: board and API answers the jobs lane saved, read from the repository, never the network.</summary>
internal static class ReadyRowFixtures
{
    /// <summary>The full path of one fixture under Fixtures/jobs.</summary>
    public static string Path(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = System.IO.Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "jobs", name);
            if (File.Exists(path))
            {
                return path;
            }
        }
        throw new InvalidOperationException("Fixtures/jobs/" + name + " not found above " + AppContext.BaseDirectory);
    }
}
