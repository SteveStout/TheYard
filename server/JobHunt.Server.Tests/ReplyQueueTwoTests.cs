using System.Text.Json;

namespace JobHunt.Server.Tests;

/// <summary>
/// Format 2 of the 4:00 AM queue, read against the fixture pair the reply lane's checker reads too
/// (check_reply_queue.py --selftest on the share holds the same two files, byte for byte).
/// </summary>
public sealed class ReplyQueueTwoTests
{
    private const string Folder = "nightly/2026-11-03/0500-reply-opus-5-5";

    [Fact]
    public void The_fixture_reads_to_the_items_both_sides_expect()
    {
        string queue = Fixture.Read("reply-queue.md");
        using var expected = JsonDocument.Parse(Fixture.Read("reply-queue.expected.json"));
        var want = expected.RootElement.GetProperty("lines").EnumerateArray()
            .Select(line => new QueueLine(Text(line, "section"), Text(line, "person"), Text(line, "company"), Text(line, "reference"), Text(line, "draft"), Text(line, "held")))
            .ToList();

        Assert.True(ReplyQueueTwo.Is(queue));
        Assert.Equal(want, ReplyQueueTwo.Lines(queue));
        Assert.Empty(ReplyQueueTwo.Unread(queue));
        Assert.Equal(0, expected.RootElement.GetProperty("unread").GetArrayLength());
    }

    [Fact]
    public void A_list_line_outside_the_grammar_is_named_and_a_note_is_not()
    {
        string queue = $"{ReplyQueueTwo.Marker}\n## B. Accepts\n4. **Jessica Khawaja**, accepted 11/8\nNote: carried from 11/7\n## D. Not owed\n- anything at all\n";

        Assert.Equal(["B: 4. **Jessica Khawaja**, accepted 11/8"], ReplyQueueTwo.Unread(queue));
        Assert.Empty(ReplyQueueTwo.Lines(queue));
    }

    [Fact]
    public void A_queue_without_the_marker_is_format_one()
    {
        Assert.False(ReplyQueueTwo.Is(Fixture.Read("linkedin-queue.md")));
    }

    [Fact]
    public async Task A_format_two_queue_from_a_later_slot_gives_its_drafts_and_its_holds_and_nothing_unread()
    {
        string tracker = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\n1,Sam Roe,AccuLynx,LinkedIn,10/27/2026,,\"CONNECTION REQUEST SENT by Claude\"\n";
        var morning = new Morning(Quiet, tracker);
        morning.Share.Files[$"{Folder}/linkedin-queue.md"] =
            $"{ReplyQueueTwo.Marker}\n# LinkedIn queue\n\n## A. Inbound\n- **Lee Markum** | - | thread: linkedin 2-x | held: text not read\n\n" +
            $"## B. Accepts\n- **Sam Roe** | AccuLynx | Count 1 | draft: {Folder}/drafts/li-followup-acculynx.txt\n" +
            "- **Steve Odell** | WellSky | Count 3406 | held: WellSky closed by Steve 11/4\n\n## C. Windows\nNote: none open\n\n## D. Not owed\n- alerts\n";
        morning.Share.Files[$"{Folder}/drafts/li-followup-acculynx.txt"] = "Sam,\n\nThanks for connecting. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to take a look?\n\nThanks,\nSteve\n";

        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);

        string done = morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/DONE.md"];
        Assert.DoesNotContain("could not read", done, StringComparison.Ordinal);
        Assert.DoesNotContain("no drafted text", done, StringComparison.Ordinal);
        Assert.Contains("Steve Odell: held by the reply lane: WellSky closed by Steve 11/4", done, StringComparison.Ordinal);
        Assert.Contains("Sam Roe", morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/worklist.tsv"], StringComparison.Ordinal);
    }

    private static string Text(JsonElement line, string name) => line.GetProperty(name).GetString() ?? string.Empty;

    private static ClaudeRun Quiet(ClaudeRequest request) => new(0, false, ResultLine.Empty);
}
