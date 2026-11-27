namespace JobHunt.Server.Tests;

/// <summary>
/// The messages worklist's held lines, settled ones retired after one morning and open ones carried (<see cref="LeftOut"/>),
/// on lines copied from J:\nightly\2026-11-27\0400-reply-opus-5-5\linkedin-queue.md, the queue behind that morning's 71
/// left-out lines (0600-messages-sonnet-5-5\DONE.md). The share is in memory (RULE 79).
/// </summary>
public sealed class LeftOutTests
{
    private const string Queue = "nightly/2026-11-03/0400-reply-opus-5-5/linkedin-queue.md";

    private const string Lines = """
        <!-- reply-queue: 2 -->

        ## B. Accepted invitations (SAMPLE 73 follow-up, the 10/22 standing permission)
        - **Hafiz Muhammad** | - | - | held: no tracker row names an invite to him, so its purpose is unread; accepted 3:46 AM CST 11/27 (gmail 1a21c660848cd288, INBOX)
        - **Babita Pal** | NinjaOne (Sr. Director Engineering) | Count 3597 | held: Steve has the last message, nothing owed (reply1123chat DONE.md, Laptop LinkedIn read 07:31 to 08:00 CST 11/23); the 11/22 follow-up draft is withdrawn so no program sends it
        - **Greg Dottavio** | Natera (Senior Director of Software Engineering) | Count 3740 | held: closed process, Natera's generic no for the Manager, Software Engineering application came 4:46 PM CST 11/20 (Count 3819); a follow-up now would ask about a closed req
        - **Ben Olsen** | Beemo Automation | Count 3883 | held: first message already sent 8:29 AM CST 11/24 on Steve's 'send' (Count 3883), naming the 11/16 direct application (Count 3672); the ball is Ben's
        - **Patrick Carpenter** | HungerRush | Count 3381 | held: the Remote Engineering Manager role is paused (Pablo Garcia 11/6, per Count 3538), and a follow-up after Steve's connect note is a second touch (RULE 25)
        - **Pragash Rajarathnam** | PetDesk | Count 3521 | held: BLACKLIST HIT, PetDesk company-wide since 1:13 PM CST 11/9 (Count 3526); his follow-up already went 11:18 AM CST 11/9 (Count 3521)

        ## C. RULE 58 follow-up windows (each only to an accepted invitee)
        - **Ben Olsen** | Beemo Automation | Count 3672 | held: Steve's first message to Ben went 8:29 AM CST 11/24 (Count 3883); the ball is Ben's
        - **Zach Schumacher** | MOXFIVE | Count 3556 | held: the accepted invitee's follow-up is section B's item (nightly/2026-11-27/0400-reply-opus-5-5/drafts/li-followup-moxfive.txt), one message and not two; Zach accepted 3:46 PM CST 11/26 (gmail 1a219d2e5456e1b2)
        - **-** | Foundation Finance | Count 3540 | held: BLACKLIST HIT, Foundation Finance company-wide and conditional (BLACKLIST.md 2026-11-10 10:22 CST, Count 3540); it lifts only if they reach out first, and Steve skipped the application 11/10 (Count 3537)
        - **-** | NetBox Labs | Count 3818 | held: no accepted invitee at NetBox Labs (no tracker row names an invite to anyone there)
        """;

    private static async Task<(Worklist List, Morning Morning)> BuildAsync(Morning? morning = null, bool dry = false)
    {
        morning ??= new Morning(_ => throw new InvalidOperationException("no lane runs in this test"));
        morning.Share.Files[Queue] = Lines;
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        var reads = await new DayReader(morning.Share).ReadAsync(new DateOnly(2026, 11, 3), default);
        return (await builder.MessagesAsync(reads, 5, dry, default), morning);
    }

    [Theory]
    [InlineData("BLACKLIST HIT, PetDesk company-wide since 1:13 PM CST 11/9 (Count 3526)", true)]
    [InlineData("closed process, Natera's generic no came 4:46 PM CST 11/20 (Count 3819)", true)]
    [InlineData("first message already sent 8:29 AM CST 11/24 (Count 3883); the ball is Ben's", true)]
    [InlineData("answered in his own thread; the ball is Kevin's", true)]
    [InlineData("thread closed, Steve's thank-you went 8:07 AM CST 11/10 (Count 3534)", true)]
    [InlineData("closed by silence, autohunt1109-5's status row (Count 3528, 11/9)", true)]
    [InlineData("no tracker row names an invite to him, so its purpose is unread", false)]
    [InlineData("the Remote Engineering Manager role is paused (Pablo Garcia 11/6)", false)]
    [InlineData("no accepted invitee at Homebound", false)]
    [InlineData("closed process, Steve closed WellSky 3:44 PM CST 11/4 (Count 3412); no tracker row names an invite to him", false)]
    public void A_reason_is_settled_or_open(string held, bool settled) => Assert.Equal(settled, LeftOut.Permanent(held));

    [Fact]
    public async Task The_settled_lines_are_listed_once_and_retired_and_the_open_ones_carried()
    {
        var (first, morning) = await BuildAsync();
        string[] retired = ["Babita Pal", "Greg Dottavio", "Ben Olsen", "Pragash Rajarathnam", "Foundation Finance"];
        foreach (string name in retired)
        {
            Assert.Single(first.Dropped, d => d.What == name && d.Reason.StartsWith("retired, settled for good: held by the reply lane: ", StringComparison.Ordinal));
        }
        foreach (string name in new[] { "Hafiz Muhammad", "Patrick Carpenter", "Zach Schumacher", "NetBox Labs" })
        {
            Assert.Single(first.Dropped, d => d.What == name && d.Reason.StartsWith("held by the reply lane: ", StringComparison.Ordinal));
        }
        string table = morning.Share.Files[WritePolicy.RetiredTable];
        Assert.Equal(5, table.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("pragash rajarathnam\t2026-11-03\tBLACKLIST HIT, PetDesk company-wide since 1:13 PM CST 11/9 (Count 3526)", table, StringComparison.Ordinal);

        var (next, _) = await BuildAsync(morning);
        Assert.DoesNotContain(next.Dropped, d => retired.Contains(d.What));
        Assert.Single(next.Dropped, d => d.What == "6 people" && d.Reason.StartsWith("retired on an earlier morning", StringComparison.Ordinal));
        Assert.Single(next.Dropped, d => d.What == "Hafiz Muhammad");
        Assert.Equal(table, morning.Share.Files[WritePolicy.RetiredTable]);
    }

    [Fact]
    public async Task A_dry_run_retires_nobody()
    {
        var (list, morning) = await BuildAsync(dry: true);
        Assert.Contains(list.Dropped, d => d.What == "Pragash Rajarathnam");
        Assert.False(morning.Share.Files.ContainsKey(WritePolicy.RetiredTable));
    }
}
