namespace JobHunt.Server.Tests;

/// <summary>
/// BLACKLIST.md in the 6 AM program (fitgate1110-f3 Part A): the day's reads take the file, and a company or a person
/// it blocks is left out of the feed, the invites, the messages and the network log's threads, the reason on the
/// Dropped row with the row's date. A job row blocks no person and no company. The rows are BLACKLIST.md's own, read
/// 11/12, cut to their shapes: the people the rows name besides the one person row are left out, and so are the
/// address and phone the person row carries.
/// </summary>
public sealed class BlacklistTests
{
    /// <summary>The file every Morning's share carries.</summary>
    internal const string File = """
        # BLACKLIST. SENDERS STEVE HAS CUT OFF. CREATED 2026-09-29 BY REPLYLANE ON HIS WORD.

        | Added | Person | Address | Company | Steve's words | Why |
        |---|---|---|---|---|---|
        | 2026-09-29 15:11 CDT | Vikas Yadav | an apetan.com address | Apetan Consulting LLC, Rutherford NJ | "Put him on the blacklist he is making no effort to pull me in or look at my value" | Mass-mail contract pitch. Red flag 6/11. |
        | 2026-11-09 13:13 CST | ALL OF PETDESK (company-wide; includes Petvisor and Vetstoria; the recruiter, the Director and every leader contacted by petdeskconnect1109) | any @petdesk.com or @petvisor.com address; LinkedIn threads with PetDesk staff | PetDesk (Remote Principal Engineer, New Products, Lever e308da7c, applied 11/5 Count 3387) | "We've put a blacklist on pet desk because they don't have us tech staff" | Company-level block. |
        | 2026-11-10 08:07 CST | THE KEYPATH EDUCATION LEAD SOFTWARE DEVELOPER JOB ONLY (job-level, not the company; its manager named) | LinkedIn job 4463107746 (Schaumburg IL, Remote) | Keypath Education | "black list thsi job" | Posted range under his floor. |
        """;

    private const string Note = "{0},\n\nYour platform work caught my eye. I cut release cycle time 50% on one .NET platform.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
    private const string Folder = "nightly/2026-11-03/0600-network-sonnet-5-5";
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static string Text(string first) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Note, first);

    private static FeedCandidate Candidate(string person, string company) =>
        new(person, company, "VP Engineering", "https://www.linkedin.com/in/someone", "B", "Tier 1, posted 11/1", "Mizzou", "LinkedIn education", "$190K-$230K posted, Greenhouse 11/1", Text(person.Split(' ')[0]), "invite");

    private static string Reason(IReadOnlyList<Dropped> dropped, string who) =>
        Assert.Single(dropped, d => d.What.StartsWith(who, StringComparison.Ordinal)).Reason;

    [Fact]
    public void The_feed_leaves_out_a_blacklisted_company_and_person_and_keeps_the_job_row_s_company()
    {
        var rows = TrackerCsvText.Parse(Fixture.Read("tracker.csv")).Skip(1).ToList();
        var gate = new PersonGate(new GateSources(rows, Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), [], File), Today);
        FeedCut cut = FeedQueue.Cut([Candidate("Pat Vet", "Petvisor"), Candidate("Vikas Yadav", "Apetan Consulting LLC"), Candidate("Lou Fine", "Keypath Education")], gate, [], FeedHolds.None);
        Assert.Equal("blacklisted: 2026-11-09", Reason(cut.Dropped, "Pat Vet"));
        Assert.Equal("blacklisted: 2026-09-29", Reason(cut.Dropped, "Vikas Yadav"));
        Assert.Equal(["Lou Fine"], cut.Kept.Select(kept => kept.Person));
    }

    [Fact]
    public async Task Invites_and_messages_leave_out_a_blacklisted_company_and_person_with_the_reason()
    {
        var morning = new Morning(_ => new ClaudeRun(0, false, ResultLine.Empty));
        morning.Config.DryRows = 10;
        morning.Share.Files["linkedin-proof/QUEUE-hotmgr-2026-11-03.md"] =
            $"### 1. Pat Vet, PetDesk (VP). Code [code].\n\n```\n{Text("Pat")}\n```\n\n"
            + $"### 2. Vikas Yadav, Apetan Consulting LLC (VP): MESSAGE\n- Profile: https://www.linkedin.com/in/vikas\n\n```\n{Text("Vikas")}\n```\n\n"
            + $"### 3. Dee Path, Keypath Education (VP). Code [code].\n\n```\n{Text("Dee")}\n```\n";
        DayReads reads = await new DayReader(morning.Share).ReadAsync(Today, default);
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);

        Worklist invites = await builder.InvitesAsync(reads, 10, dry: true, default);
        Assert.Equal("blacklisted: 2026-11-09", Reason(invites.Dropped, "Pat Vet"));
        Assert.Contains(invites.Rows, row => row.Person == "Dee Path");
        Worklist messages = await builder.MessagesAsync(reads, 10, dry: true, default);
        Assert.Equal("blacklisted: 2026-09-29", Reason(messages.Dropped, "Vikas Yadav"));
        Assert.DoesNotContain(invites.Rows.Concat(messages.Rows), row => row.Person is "Pat Vet" or "Vikas Yadav");
    }

    [Fact]
    public async Task The_network_lane_leaves_out_a_blacklisted_thread_with_the_reason()
    {
        const string pat = "Pat Lee\tAcme\thttps://www.linkedin.com/in/patlee/\t2-abc123==\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00";
        const string vet = "Jo Vet\tPetDesk\thttps://www.linkedin.com/in/jovet/\t2-vet==\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00";
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{Folder}/{NetworkLane.Threads}"] = NetworkThreads.Header + "\n" + pat + "\n" + vet + "\n";
            morning.Share.Files[$"{Folder}/threads/2-abc123==.txt"] = "Pat Lee: Good to hear from you.\nSteve: Likewise.\n";
            morning.Share.Files[$"{Folder}/threads/2-vet==.txt"] = "Jo Vet: Following up.\nSteve: Thanks.\n";
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["network"].Enabled = true;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Network), default);

        string spool = Assert.Single(morning.Share.Files.Keys, path => path.StartsWith(WritePolicy.LinkedInInbox, StringComparison.Ordinal) && path.EndsWith(".tsv", StringComparison.Ordinal));
        Assert.Equal(NetworkThreads.Header + "\n" + pat + "\n", morning.Share.Files[spool]);
        Assert.False(morning.Share.Files.ContainsKey("networklog/linkedin-inbox/text/2-vet==.txt"));
        Assert.Contains("blacklisted: 2026-11-09", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }
}
