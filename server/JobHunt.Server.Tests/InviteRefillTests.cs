namespace JobHunt.Server.Tests;

/// <summary>
/// The invites worklist refills (audit1124b F3): a block whose person the tracker shows invited is closed in its queue file
/// the morning it is skipped, so it is never offered twice, and the feed's blocks for the hiring-signal companies give the
/// lane new people through every gate it had (BLACKLIST.md, RULE 25, the day's room). Made-up people on the Morning's
/// fake share (RULE 79).
/// </summary>
public sealed class InviteRefillTests
{
    private const string Old = "linkedin-proof/QUEUE-hotmgr-2026-11-02.md";
    private const string Fresh = "linkedin-proof/QUEUE-hotmgr-2026-11-03.md";
    private static readonly DateOnly Today = new(2026, 11, 3);

    private const string Note = "{0},\n\nYour platform work caught my eye. I cut release cycle time 50% on one .NET platform.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";

    /// <summary>Yesterday's queue: two people the tracker already shows invited.</summary>
    private static string Invited() =>
        "> RULES FIRST\n\n# QUEUE-hotmgr-2026-11-02\n\n"
        + "### 1. Hal Asked, Conga (VP Engineering). Code [code].\n- Profile: https://www.linkedin.com/in/hal-asked\n- Hot: C\n\n```\n" + string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, "Hal") + "\n```\n\n"
        + "### 2. Bab Asked, NinjaOne (Director of Engineering). Code [code].\n- Profile: https://www.linkedin.com/in/bab-asked\n- Hot: C\n\n```\n" + string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, "Bab") + "\n```\n";

    private static string Tracker() => Fixture.Read("tracker.csv")
        + "3901,Hal Asked,Conga,LinkedIn,11/1/2026,,\"CONNECTION REQUEST SENT by Claude (server6am). Code abc111.\"\n"
        + "3902,Bab Asked,NinjaOne,LinkedIn,11/1/2026,,\"CONNECTION REQUEST SENT by Claude (server6am). Code abc112.\"\n"
        + "3903,Mae Messaged,Affirm,LinkedIn,10/20/2026,,\"LINKEDIN MESSAGE SENT by Claude. Code abc113.\"\n";

    private static FeedCandidate Leader(string person, string company) =>
        new(person, company, "VP Engineering", $"https://www.linkedin.com/in/{person.ToLowerInvariant().Replace(' ', '-')}", "D", $"hiring signal: {company} opened 9 seats in 7 days",
            "none found", "none", "none found", string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, person.Split(' ')[0]), "invite");

    /// <summary>A Morning with no queue but the ones a test writes.</summary>
    private static Morning Bare()
    {
        var morning = new Morning(_ => throw new InvalidOperationException("no lane runs in this test"), Tracker());
        foreach (string queue in morning.Share.Files.Keys.Where(path => path.StartsWith("linkedin-proof/QUEUE-", StringComparison.Ordinal)).ToList())
        {
            morning.Share.Files.Remove(queue);
        }
        return morning;
    }

    /// <summary>The feed's run on the hiring signal: every candidate through the feed's own gates, the kept ones written as its queue.</summary>
    private static async Task<(Morning Morning, FeedCut Cut)> RefilledAsync()
    {
        var morning = Bare();
        morning.Share.Files[Old] = Invited();
        DayReads reads = await new DayReader(morning.Share).ReadAsync(Today, default);
        FeedCut cut = FeedQueue.Cut([Leader("Ena Core", "Encora"), Leader("Pat Vet", "Petvisor"), Leader("Mae Messaged", "Affirm"), Leader("Andy Uril", "Anduril Industries")], reads.Gate, reads.Queues, reads.Holds);
        morning.Share.Files[Fresh] = FeedQueue.Render(Today, new DateTime(2026, 11, 3, 6, 5, 0), "nightly/2026-11-03/0600-feed-opus-5-5", cut.Kept);
        return (morning, cut);
    }

    [Fact]
    public async Task A_queue_of_already_invited_blocks_and_a_hiring_signal_refill_give_a_worklist_with_new_people()
    {
        var (morning, cut) = await RefilledAsync();
        Assert.Equal(["Ena Core", "Andy Uril"], cut.Kept.Select(kept => kept.Person));
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);

        Worklist list = await builder.InvitesAsync(await new DayReader(morning.Share).ReadAsync(Today, default), 10, dry: false, default);

        Assert.Equal(["Andy Uril", "Ena Core"], list.Rows.Select(row => row.Person).Order(StringComparer.Ordinal));
        Assert.Equal("already invited", Assert.Single(list.Dropped, d => d.What.StartsWith("Hal Asked", StringComparison.Ordinal)).Reason);
        Assert.Equal("already invited", Assert.Single(list.Dropped, d => d.What.StartsWith("Bab Asked", StringComparison.Ordinal)).Reason);
    }

    [Fact]
    public async Task An_already_invited_block_is_closed_in_its_queue_and_never_offered_twice()
    {
        var (morning, _) = await RefilledAsync();
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);

        await builder.InvitesAsync(await new DayReader(morning.Share).ReadAsync(Today, default), 10, dry: true, default);
        Assert.Equal(Invited(), morning.Share.Files[Old]);

        await builder.InvitesAsync(await new DayReader(morning.Share).ReadAsync(Today, default), 10, dry: false, default);
        Assert.Contains("\n- Item 1 (Hal Asked, Conga): DROPPED, already invited (the tracker shows the invite), closed by the 6 AM program 2026-11-03 so it is not offered again\n", morning.Share.Files[Old], StringComparison.Ordinal);
        Assert.Contains("\n- Item 2 (Bab Asked, NinjaOne): DROPPED, already invited", morning.Share.Files[Old], StringComparison.Ordinal);
        Assert.DoesNotContain("DROPPED", morning.Share.Files[Fresh], StringComparison.Ordinal);

        DayReads next = await new DayReader(morning.Share).ReadAsync(Today.AddDays(1), default);
        Assert.DoesNotContain(next.Queues, block => block.Source.StartsWith("QUEUE-hotmgr-2026-11-02.md", StringComparison.Ordinal));
        Worklist again = await builder.InvitesAsync(next, 10, dry: false, default);
        Assert.DoesNotContain(again.Dropped, d => d.What.StartsWith("Hal Asked", StringComparison.Ordinal));
        Assert.Equal(1, morning.Share.Files[Old].Split('\n').Count(line => line.StartsWith("- Item 1 ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_blacklist_and_rule_25_still_hold_a_refill_candidate()
    {
        var (_, cut) = await RefilledAsync();
        Assert.Contains("BLACKLIST", Assert.Single(cut.Dropped, d => d.What.StartsWith("Pat Vet", StringComparison.Ordinal)).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cut.Kept, kept => kept.Person == "Mae Messaged");
        Assert.Single(cut.Dropped, d => d.What.StartsWith("Mae Messaged", StringComparison.Ordinal));

        // A block the feed wrote before the invite went out is held at send (one invite a person) and closed; a
        // blacklisted one is held and left open, because only the tracker's invite closes a block.
        var morning = Bare();
        morning.Share.Files[Fresh] = FeedQueue.Render(Today, new DateTime(2026, 11, 3, 6, 5, 0), "nightly/2026-11-03/0600-feed-opus-5-5", [Leader("Hal Asked", "Conga"), Leader("Pat Vet", "Petvisor")]);
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        Worklist list = await builder.InvitesAsync(await new DayReader(morning.Share).ReadAsync(Today, default), 10, dry: false, default);
        Assert.Empty(list.Rows);
        Assert.Equal("already invited", Assert.Single(list.Dropped, d => d.What.StartsWith("Hal Asked", StringComparison.Ordinal)).Reason);
        Assert.NotNull(Assert.Single(list.Dropped, d => d.What.StartsWith("Pat Vet", StringComparison.Ordinal)).Reason);
        Assert.Contains("- Item 1 (Hal Asked, Conga): DROPPED, already invited", morning.Share.Files[Fresh], StringComparison.Ordinal);
        Assert.DoesNotContain("(Pat Vet, Petvisor): DROPPED", morning.Share.Files[Fresh], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_room_still_cuts_a_refill()
    {
        var (morning, _) = await RefilledAsync();
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);

        Worklist list = await builder.InvitesAsync(await new DayReader(morning.Share).ReadAsync(Today, default), 1, dry: false, default);

        Assert.Single(list.Rows);
        Assert.Equal("over the day's room, stays queued", Assert.Single(list.Dropped, d => d.Reason.StartsWith("over", StringComparison.Ordinal)).Reason);
        Assert.Single(morning.Minter.Asked);
    }
}
