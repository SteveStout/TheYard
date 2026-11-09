using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The TopicLine: the rule a clause must pass, the 4 AM lane's file as the import reads it, and the import's order,
/// where the newest file wins, a re-read changes nothing and a refused line leaves the row waiting.
/// </summary>
public sealed class NetworkTopicTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-topics-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("the platform consolidation on .NET 10", null)]
    [InlineData("the platform consolidation on .NET 10.", null)]
    [InlineData("short", "length")]
    [InlineData("the move to Azure\nand the team", "line break")]
    [InlineData("the move to Azure \u2014 and the team", "dash")]
    [InlineData("how to leverage the new platform", "tell")]
    [InlineData("whether the role is still open?", "not one clause")]
    [InlineData("the role. And the team", "not one clause")]
    [InlineData("the post at https://acme.com/jobs/1", "link")]
    [InlineData("the role", "length")]
    [InlineData("Senior platform engineer role", "the subject")]
    public void A_TopicLine_is_one_plain_clause_that_is_not_the_subject(string line, string? fails) =>
        Assert.Equal(fails, TopicLineRule.Fails(line, "RE: Senior platform engineer role"));

    [Fact]
    public void The_lane_s_file_is_a_key_and_a_clause_a_line()
    {
        var rows = TopicLineRule.Parse("PersonKey\tTopicLine\r\nPat@Acme.com\tthe platform consolidation on .NET 10\r\n\r\nbad line\nlinkedin:sam\tthe data team's move to Fabric\n", "nightly/2026-11-05/0400-reply-opus/networklog-topics.tsv");
        Assert.Equal(["pat@acme.com", "linkedin:sam"], rows.Select(r => r.PersonKey));
        Assert.Equal("the data team's move to Fabric", rows[1].TopicLine);
    }

    private void Lane(string day, string run, string text)
    {
        string folder = Path.Combine(_root, "nightly", day, run);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TopicImport.FileName), text);
    }

    private static Conversation Row(string key, long id) => new()
    {
        Id = id,
        PersonKey = key,
        FirstName = "Pat",
        FullName = "Pat Lee",
        Source = "gmail",
        FirstTwoWayAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        LastSubject = "Platform role",
    };

    [Fact]
    public async Task The_newest_file_wins_a_re_read_changes_nothing_and_a_refused_line_waits()
    {
        var rows = new NetworkFakes.Rows();
        await rows.UpsertAsync(Row("pat@acme.com", 1), DateTimeOffset.UnixEpoch, CancellationToken.None);
        await rows.UpsertAsync(Row("sam@globex.com", 2), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Lane("2026-11-04", "0400-reply-opus-5-5", "pat@acme.com\tthe first try at a clause\nsam@globex.com\twe can leverage synergy\n");
        Lane("2026-11-05", "0400-reply-opus-5-5", "pat@acme.com\tthe platform consolidation on .NET 10\nnobody@x.com\tthe data platform plans\n");
        Lane("2026-11-01", "0400-reply-opus-5-5", "pat@acme.com\ttoo old to be read at all\n");
        var import = new TopicImport(rows, new FolderMergeStore(_root));

        var first = await import.RunAsync(new DateOnly(2026, 11, 5), CancellationToken.None);
        Assert.Equal(1, first.Applied);
        Assert.Equal("the platform consolidation on .NET 10", rows.Store["pat@acme.com"].TopicLine);
        Assert.Equal("nightly/2026-11-05/0400-reply-opus-5-5/networklog-topics.tsv", rows.Store["pat@acme.com"].TopicSource);
        Assert.Null(rows.Store["sam@globex.com"].TopicLine);
        Assert.Equal(["sam@globex.com: tell", "nobody@x.com: no such row"], first.Refused);

        var again = await import.RunAsync(new DateOnly(2026, 11, 5), CancellationToken.None);
        Assert.Equal(0, again.Applied);

        Lane("2026-11-06", "0400-reply-opus-5-5", "pat@acme.com\tthe move from Framework to .NET 10\n");
        var later = await import.RunAsync(new DateOnly(2026, 11, 6), CancellationToken.None);
        Assert.Equal(1, later.Applied);
        Assert.Equal("the move from Framework to .NET 10", rows.Store["pat@acme.com"].TopicLine);
    }

    [Fact]
    public async Task No_lane_folder_is_nothing_to_import()
    {
        var result = await new TopicImport(new NetworkFakes.Rows(), new FolderMergeStore(_root)).RunAsync(new DateOnly(2026, 11, 5), CancellationToken.None);
        Assert.Equal(0, result.Applied);
        Assert.Empty(result.Refused);
    }
}
