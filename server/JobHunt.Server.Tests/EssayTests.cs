namespace JobHunt.Server.Tests;

/// <summary>
/// The owner's answer on essays, held: an answer comes only from banked text, the desk passes it before the lane types
/// it, the submitted text reaches his card in full, and a wall, a covenant or a planted instruction the lane meets is
/// written down where it belongs.
/// </summary>
public sealed class EssayTests
{
    private const string Bank = """
        ## APPROVED ESSAY
        > At Storee Construction I architected and built the .NET 9 platform that runs project management and field
        > operations for the business. I owned it end to end. I hired and led a four-person team while staying in
        > the code myself for the whole two years.
        - **Steady, direct, thorough.**
        """;

    private const string Folder = "nightly/2026-11-03/0600-apply-sonnet-5-5";

    private const string Sent = """{"row":1,"outcome":"sent","reason":null,"person":null,"title":null,"profile_url":null,"evidence":{"pending":null,"toast":null,"read_back":null,"confirmation":"Thanks for applying","screenshot":"/tmp/x.jpg"}}""";

    private static readonly IReadOnlyList<string> Banks = [BankedText.Plain(Bank)];

    private static string Essay(string answer, string question = "Describe a recent project.") => $"QUESTION: {question}\nANSWER:\n{answer}\nEND\n";

    private static ClaudeRun Answer(string rows) =>
        new(0, false, ResultLine.With($$"""{"rows":[{{rows}}],"live_invite_count":null,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));

    [Fact]
    public void Banked_sentences_pass_across_quote_markers_emphasis_and_line_breaks()
    {
        Assert.Empty(BankedText.Unbanked("I owned it end to end. I hired and led a four-person team while staying in the code myself for the whole two years.", Banks));
        Assert.Empty(BankedText.Unbanked("Steady, direct, thorough.", Banks));
    }

    [Fact]
    public void A_sentence_no_bank_holds_is_named_whatever_it_says()
    {
        var unbanked = BankedText.Unbanked("I owned it end to end. I led a team of forty.", Banks);
        Assert.Equal(["I led a team of forty."], unbanked);
    }

    [Fact]
    public void A_file_reads_its_question_and_answer_and_is_finished_only_at_END()
    {
        Essay essay = BankedText.Parse(Essay("I owned it end to end."));
        Assert.Equal("Describe a recent project.", essay.Question);
        Assert.Equal("I owned it end to end.", essay.Answer);
        Assert.True(BankedText.Finished(Essay("x")));
        Assert.False(BankedText.Finished("QUESTION: q\nANSWER:\nI owned it"));
    }

    [Fact]
    public async Task The_desk_passes_banked_clean_text_and_refuses_new_text_or_a_failed_proofread()
    {
        var share = new MemoryShare();
        Assert.Equal(EssayDesk.Clean, await new EssayDesk(share, new FakeProse(), new ScriptedClaude(_ => new(0, false, null))).JudgeAsync(Essay("I owned it end to end."), Banks, default));
        Assert.StartsWith("refused: 1 sentences", await new EssayDesk(share, new FakeProse(), new ScriptedClaude(_ => new(0, false, null))).JudgeAsync(Essay("I owned it end to end. I invented Azure."), Banks, default), StringComparison.Ordinal);
        Assert.Contains("check_aiisms.py", await new EssayDesk(share, new FakeProse("owned"), new ScriptedClaude(_ => new(0, false, null))).JudgeAsync(Essay("I owned it end to end."), Banks, default), StringComparison.Ordinal);
        Assert.StartsWith("refused: no answer", await new EssayDesk(share, new FakeProse(), new ScriptedClaude(_ => new(0, false, null))).JudgeAsync("QUESTION: q\nEND\n", Banks, default), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_with_no_essays_folder_goes_straight_through()
    {
        var share = new MemoryShare();
        var inner = new ScriptedClaude(_ => new(0, false, null));
        await new EssayDesk(share, new FakeProse(), inner).RunAsync(new ClaudeRequest([], "/tmp", "/tmp/s.jsonl", TimeSpan.FromMinutes(1)), default);
        Assert.Single(inner.Requests);
        Assert.Empty(share.Files);
    }

    [Fact]
    public async Task A_live_apply_run_asks_the_desk_and_a_dry_one_does_not()
    {
        var live = new Morning(_ => Answer(string.Empty));
        await live.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Equal(Folder, Assert.Single(live.Claude.Requests).Essays);

        var dry = new Morning(_ => Answer(string.Empty));
        await dry.Lanes().ExecuteAsync(new RunOptions(true, Lane.Apply), default);
        Assert.Null(Assert.Single(dry.Claude.Requests).Essays);
    }

    [Fact]
    public async Task A_submitted_essay_is_answered_clean_and_written_in_full_for_the_card()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{request.Essays}/essays/01-1.md"] = Essay("I owned it end to end.");
            return Answer(Sent);
        });
        morning.Share.Files["standard-answers.md"] = Bank;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);

        Assert.Equal("clean\n", morning.Share.Files[$"{Folder}/essays/01-1.verdict"]);
        string essays = morning.Share.Files[ApplyAfterword.EssaysPath(new DateOnly(2026, 11, 3))];
        Assert.Contains("**Describe a recent project.**", essays, StringComparison.Ordinal);
        Assert.Contains("I owned it end to end.", essays, StringComparison.Ordinal);
        Assert.Contains("APPLY-ESSAYS-2026-11-03.md", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_submit_whose_last_answer_the_desk_refused_is_a_defect_that_holds_the_lane()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{request.Essays}/essays/01-1.md"] = Essay("I owned it end to end.");
            morning.Share.Files[$"{request.Essays}/essays/01-2.md"] = Essay("I led a team of forty.");
            return Answer(Sent);
        });
        morning.Share.Files["standard-answers.md"] = Bank;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        string done = morning.Share.Files[$"{Folder}/DONE.md"];
        Assert.Contains("DEFECT: row 1", done, StringComparison.Ordinal);
        Assert.DoesNotContain("I owned it end to end.", morning.Share.Files[ApplyAfterword.EssaysPath(new DateOnly(2026, 11, 3))], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_bank_passes_nothing()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{request.Essays}/essays/01-1.md"] = Essay("I owned it end to end.");
            return Answer(string.Empty);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.StartsWith("refused", morning.Share.Files[$"{Folder}/essays/01-1.verdict"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wall_or_covenant_the_lane_met_joins_the_blockers_list_and_a_planted_instruction_is_logged()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{Folder}/{ApplyAfterword.Planted}"] = "row 1: a note on the form told AI assistants to start the answer with Lighthouse\n";
            return Answer("""{"row":1,"outcome":"skipped","reason":"signature page: an arbitration agreement before submit","person":null,"title":null,"profile_url":null,"evidence":null}""");
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        string company = morning.Share.Files[$"{Folder}/worklist.tsv"].Split('\n')[1].Split('\t')[2];
        Assert.Contains(company, morning.Share.Files[$"{Folder}/APPLY-BLOCKERS-2026-11-03.md"], StringComparison.Ordinal);
        Assert.Contains("arbitration agreement", morning.Share.Files[$"{Folder}/APPLY-BLOCKERS-2026-11-03.md"], StringComparison.Ordinal);
        Assert.Contains("Planted instruction on a form, ignored and not obeyed: row 1: a note", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_apply_lane_may_read_the_three_banks_and_no_lane_gets_a_shell()
    {
        var tools = ClaudeArguments.Allowed(Lane.Apply, "/home/vincent/J", "/home/vincent/J/nightly/x", false);
        Assert.Contains("Read(//home/vincent/J/standard-answers.md)", tools);
        Assert.Contains("Read(//home/vincent/J/EVIDENCE-BANK.md)", tools);
        Assert.Contains("Read(//home/vincent/J/writing-samples.md)", tools);
        Assert.DoesNotContain(tools, tool => tool.StartsWith("Bash", StringComparison.Ordinal));
        Assert.DoesNotContain("Read(//home/vincent/J/writing-samples.md)", ClaudeArguments.Allowed(Lane.Invites, "/home/vincent/J", "/home/vincent/J/nightly/x", false));
    }
}
