using System.Text.Json;

namespace JobHunt.Server.Tests;

/// <summary>The registry, the structured result, the evidence check and every record the program writes.</summary>
public sealed class ResultTests
{
    private const string Note = "Pat,\n\nMy work: theyard.stevenstout.biz/?r=abc123\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";

    private static readonly DateTimeOffset At = new(2026, 11, 3, 12, 5, 0, TimeSpan.Zero);

    private static RowResult Sent(RowEvidence? evidence, string? person = "Pat Lee") => new(1, "sent", null, person, "VP Engineering", "https://www.linkedin.com/in/pat", evidence);

    private static WorkRow Row(WorkAction action) => new(1, "Pat Lee", "Acme", action, "texts/01.txt", "abc123", "https://job-boards.greenhouse.io/acme/jobs/1", "q.md:3");

    private static LaneReport Report(LaneResult? result, IReadOnlyList<SendRecord> sends, string? held = null, bool dry = false) =>
        new(Lane.Invites, "claude-sonnet-5-5", dry, At, At.AddMinutes(9), new OutreachCounts(19, 2, 0, 2), [Row(WorkAction.Invite)], [new Dropped("Sam Roe", "Rule 25: already messaged")], result, sends, ["read the files"], held);

    private static SendRecord Verified() => new(Row(WorkAction.Invite), Sent(new RowEvidence(true, "Invitation sent to Pat.", Note, null, "/tmp/x.jpg")), Note, [], "claude-sonnet-5-5", "shots/row-01.jpg");

    private static LaneResult Clean(IReadOnlyList<RowResult> rows) => new(rows, 19, "none", null, null);

    [Fact]
    public void A_lane_with_no_marker_at_all_still_holds_the_gate()
    {
        string tail = "## LANE: mon1103 working the LinkedIn queue by hand.\n<!-- AID:20261103-113000Z-mon1103-note -->\n";
        Assert.Equal(["mon1103"], RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void A_note_on_an_open_lane_does_not_close_it_and_a_note_on_a_closed_one_does_not_open_it()
    {
        string open = "## LANE: rail1103 (LinkedIn). | IN PROGRESS |\n<!-- AID:20261103-113000Z-rail1103-registry -->\n## LANE: rail1103 NOTE sent two.\n<!-- AID:20261103-114000Z-rail1103-note -->\n";
        Assert.Equal(["rail1103"], RegistryTail.LiveOnLinkedIn(open, At));
        string closed = open + "## LANE: rail1103 CLOSED 05:50. | CLOSED |\n<!-- AID:20261103-115000Z-rail1103-close -->\n## LANE: rail1103 NOTE late.\n<!-- AID:20261103-115500Z-rail1103-note -->\n";
        Assert.Empty(RegistryTail.LiveOnLinkedIn(closed, At));
    }

    [Fact]
    public void A_row_still_in_the_spool_reads_like_a_merged_one()
    {
        string tail = RegistryTail.Stamped("20261103-113000Z-reach1103-registry", "## LANE: reach1103 (LinkedIn). | IN PROGRESS |");
        Assert.Equal(["reach1103"], RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void A_lane_closes_LinkedIn_unless_it_finished_every_row_with_no_stop()
    {
        RowResult skipped = new(1, "skipped", "moved", null, null, null, null);
        Assert.False(Report(Clean([skipped]), []).ClosesLinkedIn);
        Assert.True(Report(null, []).ClosesLinkedIn);
        Assert.True(Report(Clean([]), []).ClosesLinkedIn);
        Assert.True(Report(Clean([skipped with { Outcome = "blocked" }]), []).ClosesLinkedIn);
        Assert.True(Report(new LaneResult([skipped], null, "browser_down", null, null), []).ClosesLinkedIn);
        Assert.False((Report(null, []) with { Rows = [] }).ClosesLinkedIn);
    }

    [Fact]
    public void A_run_cut_short_closes_LinkedIn_and_is_not_clean_even_with_every_row_answered()
    {
        RowResult sent = Sent(new RowEvidence(true, "Invitation sent to Pat.", Note, null, "/tmp/x.jpg"));
        LaneReport report = Report(Clean([sent]), [Verified()]) with { CutShort = true };
        Assert.True(report.ClosesLinkedIn);
        Assert.False(report.Clean);
    }

    [Fact]
    public void A_broken_or_empty_result_line_is_no_result()
    {
        Assert.Null(LaneResult.FromLine(null));
        Assert.Null(LaneResult.FromLine("{not json"));
        Assert.Null(LaneResult.FromLine("""{"type":"result"}"""));
        Assert.NotNull(LaneResult.FromLine(ResultLine.Empty));
    }

    [Fact]
    public void A_closed_lane_does_not_hold_the_gate()
    {
        Assert.Empty(RegistryTail.LiveOnLinkedIn(Fixture.Read("state-tail.md"), At));
    }

    [Fact]
    public void An_open_LinkedIn_lane_holds_the_gate()
    {
        string tail = "## LANE: reach1103 (Claude in Chrome). Sends LinkedIn invites. | IN PROGRESS |\n<!-- AID:20261103-113000Z-reach1103-registry -->\n";
        Assert.Equal(["reach1103"], RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void An_open_lane_that_never_touches_LinkedIn_does_not_hold_it()
    {
        string tail = "## LANE: books1103 (GitHub Actions). Records only. | IN PROGRESS |\n<!-- AID:20261103-113000Z-books1103-registry -->\n";
        Assert.Empty(RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void A_lane_past_the_four_hour_ceiling_no_longer_holds_it()
    {
        string tail = "## LANE: reach1103 (Claude in Chrome). LinkedIn. | IN PROGRESS |\n<!-- AID:20261103-070000Z-reach1103-registry -->\n";
        Assert.Empty(RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void A_lane_s_close_row_ends_it_even_if_the_close_says_nothing_of_LinkedIn()
    {
        string tail = "## LANE: reach1103 (Chrome). LinkedIn. | IN PROGRESS |\n<!-- AID:20261103-113000Z-reach1103-registry -->\n\n## LANE: reach1103 CLOSED 05:50. Done. | CLOSED |\n<!-- AID:20261103-115000Z-reach1103-close -->\n";
        Assert.Empty(RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void This_process_s_own_row_is_the_lock_s_job()
    {
        string tail = RegistryRows.Live(At, false, "invites") + "<!-- AID:20261103-113000Z-server6am-registry -->\n";
        Assert.Empty(RegistryTail.LiveOnLinkedIn(tail, At));
    }

    [Fact]
    public void Registry_rows_take_the_house_shape()
    {
        Assert.Equal("inbox/append-inbox/state.md/20261103-120500Z-server6am-registry.md", RegistryRows.Path(At, "registry"));
        Assert.StartsWith("## LANE: server6am1103-0605 ", RegistryRows.Live(At, true, "invites, apply"), StringComparison.Ordinal);
        Assert.EndsWith("| IN PROGRESS |\n", RegistryRows.Live(At, true, "invites, apply"), StringComparison.Ordinal);
        Assert.EndsWith("| CLOSED |\n", RegistryRows.Closed(At, At, "line one\nline two"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_schema_is_valid_json_and_a_lane_s_answer_parses()
    {
        using JsonDocument schema = JsonDocument.Parse(LaneResultSchema.Json);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        LaneResult? result = LaneResult.Parse("""{"rows":[{"row":1,"outcome":"sent","reason":null,"person":"Pat Lee","title":"VP","profile_url":"u","evidence":{"pending":true,"toast":"t","read_back":"r","confirmation":null,"screenshot":"s"}}],"live_invite_count":19,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}""");
        Assert.NotNull(result);
        Assert.Equal("Pat Lee", result.Rows[0].Person);
        Assert.True(result.Rows[0].Evidence?.Pending);
    }

    [Fact]
    public void A_broken_answer_is_no_result()
    {
        Assert.Null(LaneResult.Parse("{not json"));
    }

    [Theory]
    [InlineData("weekly_limit", true)]
    [InlineData("checkpoint", true)]
    [InlineData("captcha", true)]
    [InlineData("sign_in", true)]
    [InlineData("restriction", true)]
    [InlineData("browser_down", false)]
    [InlineData("none", false)]
    public void The_LinkedIn_stops_are_the_five_the_red_list_names(string condition, bool stops)
    {
        Assert.Equal(stops, new LaneResult([], null, condition, null, null).StopsLinkedIn);
    }

    [Fact]
    public void An_invite_stands_on_pending_toast_note_and_screenshot()
    {
        Assert.Empty(EvidenceCheck.Missing(WorkAction.Invite, Note, Sent(new RowEvidence(true, "Invitation sent to Pat.", Note.Replace("\n\n", "\n"), null, "/tmp/x.jpg"))));
    }

    [Fact]
    public void The_model_s_word_is_not_a_verification()
    {
        var missing = EvidenceCheck.Missing(WorkAction.Invite, Note, Sent(null));
        Assert.Equal(4, missing.Count);
    }

    [Fact]
    public void A_toast_for_someone_else_and_an_edited_note_both_fail()
    {
        var missing = EvidenceCheck.Missing(WorkAction.Invite, Note, Sent(new RowEvidence(true, "Invitation sent to Sam.", Note + " extra", null, "/tmp/x.jpg")));
        Assert.Equal(["toast did not name the person", "note read back differs from the worklist text"], missing);
    }

    [Fact]
    public void A_message_must_come_back_with_its_blank_lines()
    {
        Assert.Empty(EvidenceCheck.Missing(WorkAction.Message, Note, Sent(new RowEvidence(null, null, Note, null, "/tmp/x.jpg"))));
        Assert.Equal(["sent bubble differs from the worklist text"], EvidenceCheck.Missing(WorkAction.Message, Note, Sent(new RowEvidence(null, null, Note.Replace("\n\n", "\n"), null, "/tmp/x.jpg"))));
    }

    [Fact]
    public void An_application_stands_on_its_confirmation_page()
    {
        Assert.Empty(EvidenceCheck.Missing(WorkAction.Apply, string.Empty, Sent(new RowEvidence(null, null, null, "Thanks for applying", "/tmp/x.jpg"))));
        Assert.Equal(["no confirmation page"], EvidenceCheck.Missing(WorkAction.Apply, string.Empty, Sent(new RowEvidence(null, null, null, " ", "/tmp/x.jpg"))));
    }

    [Fact]
    public void The_name_is_all_the_lane_may_fill()
    {
        Assert.Equal("Pat,\nHi", EvidenceCheck.Filled("[First name],\nHi", "Pat Lee"));
        Assert.Equal("[First name],", EvidenceCheck.Filled("[First name],", null));
    }

    [Fact]
    public void A_tracker_row_parses_back_in_six_columns_and_counts_as_what_it_is()
    {
        string line = TrackerRowFile.Line(Verified(), At);
        string[] cells = TrackerCsvText.Parse(line).Single();
        Assert.Equal(6, cells.Length);
        Assert.Equal("Pat Lee", cells[0]);
        Assert.Equal("LinkedIn", cells[2]);
        Assert.Equal("11/3/2026", cells[3]);
        Assert.Contains("Would it be a bad idea", cells[5], StringComparison.Ordinal);
        Assert.Contains("Code abc123.", cells[5], StringComparison.Ordinal);
        Assert.True(TrackerKinds.IsInvite(TrackerKinds.Seven(cells)));
    }

    [Fact]
    public void An_unverified_send_says_so_on_its_row()
    {
        SendRecord thin = Verified() with { Missing = ["no screenshot"] };
        Assert.Contains("UNVERIFIED", TrackerCsvText.Parse(TrackerRowFile.Line(thin, At)).Single()[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/1", "Greenhouse")]
    [InlineData("https://jobs.lever.co/acme/1", "Lever")]
    [InlineData("https://jobs.ashbyhq.com/acme/1", "Ashby")]
    [InlineData("https://www.linkedin.com/jobs/view/1/", "LinkedIn Easy Apply")]
    public void An_application_row_names_its_board_and_counts_for_Rule_50(string url, string board)
    {
        var record = new SendRecord(Row(WorkAction.Apply) with { Url = url }, Sent(new RowEvidence(null, null, null, "Thanks", "s"), null), string.Empty, [], "claude-sonnet-5-5", "shots/row-01.jpg");
        string[] cells = TrackerCsvText.Parse(TrackerRowFile.Line(record, At)).Single();
        Assert.Equal(board, cells[2]);
        Assert.True(TrackerRows.IsApplication(TrackerKinds.Seven(cells)));
    }

    [Fact]
    public void A_spool_file_s_name_is_stamped_and_slugged()
    {
        Assert.Equal("20261103-120500Z-server6am-pat-lee.csv", TrackerRowFile.Name(At, "Pat Lee"));
    }

    [Fact]
    public void The_done_note_carries_every_row_skip_ceiling_and_time()
    {
        string note = DoneNote.Render(Report(Clean([Sent(null)]), [Verified()]));
        Assert.StartsWith("> RULES FIRST", note, StringComparison.Ordinal);
        Assert.Contains("| 1 | Pat Lee | Acme | sent | verified, screenshot shots/row-01.jpg |", note, StringComparison.Ordinal);
        Assert.Contains("Sam Roe: Rule 25: already messaged", note, StringComparison.Ordinal);
        Assert.Contains("19 invites in 7 days", note, StringComparison.Ordinal);
        Assert.Contains("After this run: 20.", note, StringComparison.Ordinal);
        Assert.Contains("wall 9.0 minutes", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stop_condition_and_a_hold_reach_the_done_note()
    {
        string stopped = DoneNote.Render(Report(new LaneResult([], 19, "captcha", "A check appeared.", "/tmp/s.jpg"), []));
        Assert.Contains("## Stop condition: captcha", stopped, StringComparison.Ordinal);
        Assert.Contains("HELD: reach1103 LIVE on LinkedIn", DoneNote.Render(Report(null, [], "reach1103 LIVE on LinkedIn")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_never_runs_past_six_lines_and_never_asks()
    {
        LaneReport report = Report(Clean([Sent(null)]), [Verified()]);
        string card = RunCard.Render(new DateOnly(2026, 11, 3), false, [report, report, report], ["one?", "two", "three", "four"]);
        string[] lines = card.TrimEnd().Split('\n');
        Assert.True(lines.Length <= RunCard.MostLines);
        Assert.DoesNotContain("?", card, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_line_says_what_went_out_and_what_stopped()
    {
        string card = RunCard.Render(new DateOnly(2026, 11, 3), false, [Report(new LaneResult([], 19, "captcha", null, null), [Verified()])], []);
        Assert.Contains("invites: 1 sent verified of 1", card, StringComparison.Ordinal);
        Assert.Contains("20 invites in 7 days", card, StringComparison.Ordinal);
        Assert.Contains("STOPPED: captcha", card, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_and_a_held_card_say_so()
    {
        Assert.Contains("DRY RUN, nothing sent", RunCard.Render(new DateOnly(2026, 11, 3), true, [Report(Clean([]), [], dry: true)], []), StringComparison.Ordinal);
        Assert.Contains("invites: held, a reason", RunCard.Render(new DateOnly(2026, 11, 3), false, [Report(null, [], "a reason")], []), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unverified_send_makes_a_run_unclean_and_says_so_on_the_card()
    {
        SendRecord thin = Verified() with { Missing = ["no screenshot"] };
        LaneReport report = Report(Clean([Sent(null)]), [thin]);
        Assert.False(report.Clean);
        Assert.Contains("1 UNVERIFIED", RunCard.Render(new DateOnly(2026, 11, 3), false, [report], []), StringComparison.Ordinal);
    }

    [Fact]
    public void Five_clean_runs_in_a_row_make_a_lane_eligible()
    {
        LaneReport clean = Report(Clean([Sent(null)]), [Verified()]);
        string line = CleanRuns.Line(new DateOnly(2026, 11, 3), clean)!;
        Assert.Equal("2026-11-03\tinvites\tclaude-sonnet-5-5\tclean\t1\n", line);
        string four = string.Concat(Enumerable.Repeat(line, 4));
        Assert.False(CleanRuns.Eligible(four, Lane.Invites, "claude-sonnet-5-5"));
        Assert.True(CleanRuns.Eligible(four + line, Lane.Invites, "claude-sonnet-5-5"));
        Assert.False(CleanRuns.Eligible(four + line, Lane.Messages, "claude-sonnet-5-5"));
        Assert.False(CleanRuns.Eligible(four + line.Replace("clean", "defect"), Lane.Invites, "claude-sonnet-5-5"));
        Assert.False(CleanRuns.Eligible(four + line + "2026-11-04\tinvites\tclaude-sonnet-5-5\tdefect\t1\n", Lane.Invites, "claude-sonnet-5-5"));
    }

    [Fact]
    public void A_dry_run_and_a_run_with_no_send_add_no_line()
    {
        Assert.Null(CleanRuns.Line(new DateOnly(2026, 11, 3), Report(Clean([]), [])));
        Assert.Null(CleanRuns.Line(new DateOnly(2026, 11, 3), Report(Clean([]), [Verified()], dry: true)));
    }

    [Fact]
    public void A_run_with_a_stop_or_no_result_is_not_clean()
    {
        Assert.False(Report(new LaneResult([], 19, "captcha", null, null), [Verified()]).Clean);
        Assert.False(Report(null, [Verified()]).Clean);
        Assert.False(Report(Clean([]), [Verified()], "held").Clean);
        Assert.False(Report(Clean([]), []).Clean);
        Assert.True(Report(Clean([]), [Verified()]).Clean);
    }

    [Fact]
    public void Invites_sent_with_no_live_count_are_a_defect_on_the_note_the_card_and_the_table()
    {
        LaneReport report = Report(new LaneResult([], null, "none", null, null), [Verified()]);
        Assert.True(report.NoRecount);
        Assert.False(report.Clean);
        Assert.Contains("DEFECT: invites went out with no live count", DoneNote.Render(report), StringComparison.Ordinal);
        Assert.Contains("DEFECT: no live count read first", RunCard.Render(new DateOnly(2026, 11, 3), false, [report], []), StringComparison.Ordinal);
        Assert.Contains("defect", CleanRuns.Line(new DateOnly(2026, 11, 3), report), StringComparison.Ordinal);
        Assert.False((report with { Lane = Lane.Apply }).NoRecount);
    }

    [Fact]
    public void The_model_s_note_reaches_the_done_note_with_no_stop()
    {
        string note = DoneNote.Render(Report(new LaneResult([], null, "none", "Recount incomplete.", null), []));
        Assert.Contains("## The model's note", note, StringComparison.Ordinal);
        Assert.Contains("Recount incomplete.", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_lane_with_no_model_says_so_on_the_card_and_a_lost_run_says_read_done()
    {
        string lost = RunCard.Render(new DateOnly(2026, 11, 3), false, [Report(null, [])], []);
        Assert.Contains("no result from the model, read DONE.md", lost, StringComparison.Ordinal);
        string done = DoneNote.Render(Report(null, []));
        Assert.Contains("| 1 | Pat Lee | Acme | no result |", done, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_the_lane_found_replaces_the_blank_on_the_row_and_the_note()
    {
        WorkRow blank = Row(WorkAction.Invite) with { Person = string.Empty };
        var found = new SendRecord(blank, Sent(new RowEvidence(true, "Invitation sent to Sam.", Note, null, "s"), "Sam Roe"), Note, [], "m", "s");
        Assert.Equal("Sam Roe", TrackerCsvText.Parse(TrackerRowFile.Line(found, At)).Single()[0]);
        var unnamed = found with { Reported = found.Reported with { Person = null } };
        Assert.Equal("NA", TrackerCsvText.Parse(TrackerRowFile.Line(unnamed, At)).Single()[0]);
        var report = Report(Clean([found.Reported]), [found]) with { Rows = [blank] };
        Assert.Contains("| 1 | Sam Roe | Acme |", DoneNote.Render(report), StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_cell_in_a_note_keeps_the_table_whole()
    {
        var bad = new RowResult(1, "skipped", "a | b\nc", "Pat Lee", null, null, null);
        Assert.Contains("| a / b c |", DoneNote.Render(Report(Clean([bad]), [])), StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_is_cut_as_nightly_yml_cuts_it_and_posted_as_one_document()
    {
        string cut = UsageCut.Json(Fixture.Read("result-line.json"));
        using JsonDocument usage = JsonDocument.Parse(cut);
        Assert.Equal(52, usage.RootElement.GetProperty("num_turns").GetInt32());
        Assert.False(usage.RootElement.TryGetProperty("type", out _));

        using JsonDocument document = JsonDocument.Parse(UsageCut.Document(cut, Report(null, [], dry: true), "run1"));
        Assert.Equal("WickedAwsome, 6 AM lanes", document.RootElement.GetProperty("lane").GetString());
        Assert.Equal(29847, document.RootElement.GetProperty("tokens_out").GetInt64());
        Assert.Equal("invites-dry", document.RootElement.GetProperty("mode").GetString());
        Assert.Equal("0600", document.RootElement.GetProperty("slot").GetString());
    }

    private static WorkRow Named(string person, string avoid = "") => new(1, person, "Acme", WorkAction.Invite, "texts/01.txt", "abc123", string.Empty, "q:1", avoid);

    [Theory]
    [InlineData("Pat Lee", "Pat Lee", true)]
    [InlineData("Pat Lee", "pat  lee", true)]
    [InlineData("Pat Lee", "Pat Lee, MBA", true)]
    [InlineData("Pat Lee", "Mallory Other", false)]
    public void A_named_row_is_held_to_its_name(string worklist, string reported, bool same)
    {
        Assert.Equal(same, EvidenceCheck.WrongPerson(Named(worklist), Sent(null) with { Person = reported }) is null);
    }

    [Fact]
    public void A_name_the_lane_picked_is_held_to_the_avoid_column()
    {
        Assert.NotNull(EvidenceCheck.WrongPerson(Named(string.Empty, "Eddie Salas; Sam Roe"), Sent(null) with { Person = "Sam Roe" }));
        Assert.Null(EvidenceCheck.WrongPerson(Named(string.Empty, "Eddie Salas; Sam Roe"), Sent(null) with { Person = "New Person" }));
    }

    [Theory]
    [InlineData("Andrea (Dre) Hazel", "Andrea Hazel")]
    [InlineData("Dr. Patrick Goldberg", "Patrick Goldberg")]
    [InlineData("Amanda Petrone / Jenny George", "Jenny George")]
    [InlineData("Kirk Wagner, Jr.", "Kirk Wagner")]
    public void The_avoid_column_matches_a_name_however_the_tracker_wrote_it(string avoid, string reported)
    {
        Assert.NotNull(EvidenceCheck.WrongPerson(Named(string.Empty, avoid), Sent(null) with { Person = reported }));
    }
}
