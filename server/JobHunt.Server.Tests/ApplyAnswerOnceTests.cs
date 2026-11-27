namespace JobHunt.Server.Tests;

/// <summary>
/// The 23 November apply morning (J:\nightly\2026-11-23\0600-apply-sonnet-5-5\worklist.tsv and DONE.md) held against
/// today's builder, and the two things the morning left undone: a question with no banked answer is put to Steve once on
/// the day's blockers list (<see cref="Blockers.AnswerOnce"/>), and the posting comes off the skip list the day
/// standard-answers.md changes (<see cref="ApplySkips.Answered"/>). The share is in memory (RULE 79).
/// </summary>
public sealed class ApplyAnswerOnceTests
{
    private const string Folder = "nightly/2026-11-03/0600-apply-sonnet-5-5";

    private const string DentalLink = "https://job-boards.greenhouse.io/acmedental/jobs/6222461004";

    // JOBS-2026-11-23.md lines 10, 11, 14 and 20 and JOBS-2026-11-21.md line 11, their READY TO APPLY rows as written,
    // with one more Anduril posting of that morning's 14 to hold one application a company a morning.
    private const string Jobs = """
        ## READY TO APPLY
        | # | Company | Role | Link | Board | Band | Posted | Notes |
        |---|---|---|---|---|---|---|---|
        | 1 | Meridianlink | Director, Software Engineering - Consumer Portal | https://jobs.ashbyhq.com/meridianlink/b81d3423-bb51-4e88-99ec-18fab781c8c1 | ashby | $167,778-$262,200, Ashby compensation field, read 02:05 CST, PASS | 2026-11-02 (1 days) | |
        | 2 | Anduril Industries | Senior Mission Planning Engineer | https://job-boards.greenhouse.io/andurilindustries/jobs/5303810007 | greenhouse | $191,000-$254,000, printed in the Greenhouse posting text, read 02:05 CST, PASS | 2026-11-02 (1 days) | |
        | 3 | Anduril Industries | Software Engineering Manager (Factory Data) | https://job-boards.greenhouse.io/andurilindustries/jobs/5303810008 | greenhouse | PASS | 2026-11-02 (1 days) | |
        | 5 | 1Password | Engineering Manager, Infrastructure (Device Trust) | https://jobs.ashbyhq.com/1password/f0100f8b-ca22-4cf3-abd8-fda500de8d9e | ashby | $192,000-$278,000 USD base, printed in the Ashby posting text, read 02:05 CST, PASS | 2026-11-02 (1 days) | |
        | 11 | Esri | Principal Software Development Engineer - National Imagery | https://job-boards.greenhouse.io/esri/jobs/5218015007 | greenhouse | $150,696-$247,312, printed in the Greenhouse posting text, read 02:05 CST, PASS | 2026-11-02 (1 days) | |
        | 2 | Care Access | Senior Manager, Engineering | https://job-boards.greenhouse.io/careaccess/jobs/4512749009 | greenhouse | $170,000-$220,000, printed in the Greenhouse posting text, read 02:08 CST, PASS | 2026-11-02 (1 days) | |
        """;

    // nightly/jobs-candidates-2026-11-23.tsv, its header and lines 3, 6, 7 and 55 as the 2 AM pick wrote them (dated to the test's day).
    private const string Candidates = "Source\tCompany\tTitle\tLevel\tUrl\tBoardKind\tPosted\tDaysOld\tBand\tBandFlag\tRemote\tConfirmed\tStack\tTrackerNote\tWhyPassed\tHold\tForm\n"
        + "feed:ashby\tM Kopa\tHR Shared Services Lead\tLead\thttps://jobs.ashbyhq.com/M-KOPA/ebab596b-350b-45bc-8c53-7d1943fefaae\tashby\t2026-11-02\t3\t\tNONE\tremote\tfeed\tai lead\tno applications in 60 days\tremote; Lead; ai lead; band NONE; 3 days\t\tFORM: essay required\n"
        + "feed:ashby\tRedpanda Data\tSenior Manager, Customer Engineering\tManager\thttps://jobs.ashbyhq.com/redpanda-data/f58beeb6-28f8-4307-b837-40e00e552b53\tashby\t2026-11-02\t3\t\tNONE\tremote\tfeed\tai lead\tno applications in 60 days\tremote; Manager; ai lead; band NONE; 3 days\t\t\n"
        + "feed:greenhouse\tElite Technology\tPrincipal Technical Support Analyst\tPrincipal\thttps://job-boards.greenhouse.io/elitetechnology/jobs/5489475008\tgreenhouse\t2026-11-02\t3\t\tNONE\tremote\tfeed\tai lead\t2 in 60 days (Counts 3449, 3455)\tremote; Principal; ai lead; band NONE; 3 days\t\t\n"
        + "feed:greenhouse\tNorth Point Technology\tSAP Technical Lead\tLead\thttps://job-boards.greenhouse.io/northpointtechnology/jobs/8957864002\tgreenhouse\t2026-11-02\t2\t\tNONE\tnot remote: Novi, Michigan, United States\tfeed\tai lead\tno applications in 60 days\tnot remote: Novi, Michigan, United States; Lead; ai lead; band NONE; 2 days\t\t\n";

    private static ClaudeRun Quiet(ClaudeRequest request) => new(0, false, ResultLine.Empty);

    private static ClaudeRun Skipped(string reason) =>
        new(0, false, ResultLine.With($$"""{"rows":[{"row":1,"outcome":"skipped","reason":"{{reason}}","person":null,"title":null,"profile_url":null,"evidence":null}],"live_invite_count":null,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));

    private static async Task<Morning> RunAsync(Func<ClaudeRequest, ClaudeRun> script, Action<Morning>? seed = null)
    {
        var morning = new Morning(script);
        seed?.Invoke(morning);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        return morning;
    }

    [Fact]
    public async Task The_23_November_rows_on_todays_builder_drop_the_HR_seat_and_the_support_analyst_and_send_one_Anduril_row()
    {
        var morning = new Morning(_ => throw new InvalidOperationException("no lane runs in this test"));
        morning.Share.Files.Remove("APPLY-SWEEP-2026-10-28-ainative.md");
        morning.Share.Files["leads/scored.tsv"] = string.Empty;
        morning.Share.Files["JOBS-2026-11-03.md"] = Jobs;
        morning.Share.Files[FreshPostings.PathFor(new DateOnly(2026, 11, 3))] = Candidates;
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        var reads = await new DayReader(morning.Share).ReadAsync(new DateOnly(2026, 11, 3), default);

        var list = await builder.ApplyAsync(reads, null, false, default);

        // What still reaches the model: every READY row once a company, Redpanda and North Point. The pay floor is the 2 AM
        // pick's (the band column); M Kopa's "HR" and Elite's support-analyst title are dropped by RoleTitle as the
        // candidates file is read, so neither reaches the model again. North Point is onsite in Michigan, which RULE 76 lets
        // in, and its band is unknown in code: it still reaches the model.
        Assert.Equal(["1Password", "Anduril Industries", "Care Access", "Esri", "Meridianlink", "North Point Technology", "Redpanda Data"], list.Rows.Select(row => row.Company).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(list.Rows, row => row.Company is "M Kopa" or "Elite Technology");
        Assert.Single(list.Rows, row => row.Company == "Anduril Industries");
        Assert.Contains(list.Dropped, d => d.What == "Anduril Industries, Software Engineering Manager (Factory Data)" && d.Reason == "one application a company a morning");
        Assert.Equal("not an engineering role: HR", RoleTitle.Failure("HR Shared Services Lead"));
        Assert.Equal("not an engineering role: no engineering role word", RoleTitle.Failure("Principal Technical Support Analyst"));
    }

    [Theory]
    [InlineData("no banked answer: country of birth and prior Anduril application unverified; also requires 5+ years Mission Planning", "country of birth and prior Anduril application unverified; also requires 5+ years Mission Planning")]
    [InlineData("no banked answer: outside employment intent question", "outside employment intent question")]
    [InlineData("no banked answer: experience in cybersecurity SaaS, close personal relationships; plus essays", "experience in cybersecurity SaaS, close personal relationships; plus essays")]
    [InlineData("no banked answer: distance from Novi, Michigan or Maumee, Ohio; role needs 10 years SAP Basis", "distance from Novi, Michigan or Maumee, Ohio; role needs 10 years SAP Basis")]
    public async Task A_question_with_no_banked_answer_is_put_to_him_once_on_the_days_blockers_list(string reason, string question)
    {
        Morning morning = await RunAsync(_ => Skipped(reason));
        string blockers = morning.Share.Files[$"{Folder}/APPLY-BLOCKERS-2026-11-03.md"];
        Assert.Contains($"- Where it stuck: answer once: {question}.", blockers, StringComparison.Ordinal);
        Assert.Contains($"- Link: {DentalLink}", blockers, StringComparison.Ordinal);
        Assert.Contains("1 questions with no banked answer added to the day's blockers list as \"answer once:\" items", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
        Assert.Contains($"{DentalLink}\t2026-11-03\t{reason}\n", morning.Share.Files[WritePolicy.SkipsTable], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_skip_puts_nothing_on_the_list_as_answer_once()
    {
        Morning morning = await RunAsync(_ => Skipped("onsite question: relocate to Dallas, yes or no"));
        Assert.DoesNotContain("answer once: ", morning.Share.Files.GetValueOrDefault($"{Folder}/APPLY-BLOCKERS-2026-11-03.md", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_posting_comes_back_the_day_standard_answers_changes()
    {
        const string Skip = $"{DentalLink}\t2026-11-02\tno banked answer: outside employment intent question\n";
        Morning before = await RunAsync(Quiet, m =>
        {
            m.Share.Files[WritePolicy.SkipsTable] = Skip;
            m.Share.Files["standard-answers.md"] = "bank";
            m.Share.Changed["standard-answers.md"] = new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero);
        });
        Assert.DoesNotContain("Acme Dental", before.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);

        Morning answered = await RunAsync(Quiet, m =>
        {
            m.Share.Files[WritePolicy.SkipsTable] = Skip;
            m.Share.Files["standard-answers.md"] = "bank";
            m.Share.Changed["standard-answers.md"] = new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero);
        });
        Assert.Contains("Acme Dental", answered.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);

        Morning other = await RunAsync(Quiet, m =>
        {
            m.Share.Files[WritePolicy.SkipsTable] = $"{DentalLink}\t2026-11-02\tsignature page: an arbitration agreement\n";
            m.Share.Files["standard-answers.md"] = "bank";
            m.Share.Changed["standard-answers.md"] = new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero);
        });
        Assert.DoesNotContain("Acme Dental", other.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public void Answered_reads_only_unbanked_skips_on_or_before_the_change()
    {
        string table = $"{DentalLink}\t2026-11-02\tno banked answer: x\nhttps://x.example/late\t2026-11-03\tno banked answer: y\nhttps://x.example/wall\t2026-11-02\taccount wall\n";
        Assert.Equal([ApplySkips.Key(DentalLink)], ApplySkips.Answered(table, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 2)));
    }
}
