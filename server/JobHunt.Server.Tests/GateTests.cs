namespace JobHunt.Server.Tests;

/// <summary>The slot, the write policy, the ceilings, the counts and the gates, read from fixtures cut from the real files.</summary>
public sealed class GateTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);

    private static IReadOnlyList<string[]> Rows() => [.. TrackerCsvText.Parse(Fixture.Read("tracker.csv")).Skip(1)];

    private static PersonGate Gate(params string[] agencies) =>
        new(new GateSources(Rows(), Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), agencies), Today);

    [Theory]
    [InlineData("claude-sonnet-5-5", false, "nightly/2026-11-02/0600-invites-sonnet-5-5")]
    [InlineData("claude-haiku-5-5", true, "nightly/2026-11-02/0600-invites-haiku-5-5-dry")]
    public void The_run_folder_takes_nightly_yml_s_shape(string model, bool dry, string expected)
    {
        Assert.Equal(expected, RunSlot.RunFolder(Today, Lane.Invites, model, dry));
    }

    [Fact]
    public void The_day_is_Central_on_both_sides_of_midnight_UTC()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), RunSlot.Day(new DateTimeOffset(2026, 11, 3, 5, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 3), RunSlot.Day(new DateTimeOffset(2026, 11, 3, 6, 1, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("invites", Lane.Invites)]
    [InlineData("MESSAGES", Lane.Messages)]
    [InlineData("apply", Lane.Apply)]
    public void A_lane_parses_from_its_word(string word, Lane lane)
    {
        Assert.Equal(lane, RunSlot.Parse(word));
    }

    [Fact]
    public void An_unknown_lane_word_is_null()
    {
        Assert.Null(RunSlot.Parse("everything"));
    }

    [Fact]
    public void Lanes_have_their_docs_and_the_first_two_are_LinkedIn()
    {
        Assert.Equal("LINKEDIN-INVITES-LANE.md", RunSlot.Doc(Lane.Invites));
        Assert.Equal("LINKEDIN-MESSAGES-LANE.md", RunSlot.Doc(Lane.Messages));
        Assert.Equal("APPLY-LANE.md", RunSlot.Doc(Lane.Apply));
        Assert.True(RunSlot.OnLinkedIn(Lane.Messages));
        Assert.False(RunSlot.OnLinkedIn(Lane.Apply));
    }

    [Theory]
    [InlineData("nightly/2026-11-02/0600-invites-sonnet-5-5/DONE.md", true)]
    [InlineData("nightly/2026-11-02/0600-card.md", true)]
    [InlineData("nightly/server6am-runs.tsv", true)]
    [InlineData("inbox/tracker-inbox/20261103-120000Z-server6am-x.csv", true)]
    [InlineData("inbox/append-inbox/state.md/20261103-120000Z-server6am-registry.md", true)]
    [InlineData("Job_Hunt_Tracker.csv", false)]
    [InlineData("state.md", false)]
    [InlineData("responders.md", false)]
    [InlineData("writing-samples.md", false)]
    [InlineData("failure-log.md", false)]
    [InlineData("nightly/LEADS.md", false)]
    [InlineData("nightly/2026-11-02/../../state.md", false)]
    [InlineData("inbox/tracker-inbox/merged/x.csv", false)]
    [InlineData("inbox/tracker-inbox/x.md", false)]
    public void The_program_writes_only_its_run_folders_and_the_two_inboxes(string path, bool allowed)
    {
        Assert.Equal(allowed, WritePolicy.Allowed(path));
    }

    [Fact]
    public void Ceilings_are_read_from_the_files_that_own_them()
    {
        Assert.Equal(new Ceilings(14, 26, 40, 98, 90), Ceilings.Read(Fixture.Read("limits.md"), Fixture.Read("conventions.md")));
    }

    [Fact]
    public void A_ceiling_that_cannot_be_read_closes_the_gate()
    {
        Assert.Throws<FormatException>(() => Ceilings.Read("no table", Fixture.Read("conventions.md")));
        Assert.Throws<FormatException>(() => Ceilings.Read(Fixture.Read("limits.md"), "no pause"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Tracker_rows_read_as_their_kind(int index, bool linkedInOutbound)
    {
        string[] row = Rows()[index];
        Assert.Equal(linkedInOutbound, TrackerKinds.IsInvite(row) || TrackerKinds.IsMessage(row));
    }

    [Fact]
    public void A_field_with_a_newline_stays_one_row()
    {
        Assert.Contains("Profile linkedin.com/in/bo-example", Rows()[0][6], StringComparison.Ordinal);
        Assert.Equal("3316", Rows()[1][0]);
    }

    [Fact]
    public void A_row_on_the_invite_channel_is_an_invite_whatever_its_notes_say()
    {
        Assert.True(TrackerKinds.IsInvite(Rows()[5]));
    }

    [Fact]
    public void A_spool_row_of_six_cells_reads_as_seven()
    {
        Assert.Equal(7, TrackerKinds.Seven(["Pat Lee", "Acme", "LinkedIn", "11/2/2026", "", "note"]).Length);
    }

    [Fact]
    public void Counts_cover_the_rolling_week_and_the_day()
    {
        OutreachCounts counts = OutreachCounts.From(Rows(), Today);
        Assert.Equal(new OutreachCounts(3, 2, 0, 2), counts);
    }

    [Fact]
    public void Room_is_the_tightest_of_the_three_ceilings()
    {
        var ceilings = new Ceilings(14, 26, 40, 98, 90);
        Assert.Equal(12, new OutreachCounts(19, 2, 0, 2).InviteRoom(ceilings));
        Assert.Equal(3, new OutreachCounts(86, 0, 0, 0).InviteRoom(ceilings));
        Assert.Equal(0, new OutreachCounts(89, 0, 0, 0).InviteRoom(ceilings));
        Assert.Equal(0, new OutreachCounts(95, 0, 0, 0).InviteRoom(ceilings));
        Assert.Equal(1, new OutreachCounts(10, 5, 34, 39).InviteRoom(ceilings));
    }

    [Fact]
    public void Messages_room_leaves_what_the_invites_used()
    {
        var ceilings = new Ceilings(14, 26, 40, 98, 90);
        Assert.Equal(26, new OutreachCounts(0, 0, 0, 0).MessageRoom(ceilings, 0));
        Assert.Equal(5, new OutreachCounts(0, 0, 20, 25).MessageRoom(ceilings, 10));
    }

    [Theory]
    [InlineData("CONNECTION REQUEST SENT by Claude", true, false)]
    [InlineData("SCOPED CONNECTION REQUEST SENT with a note", true, false)]
    [InlineData("SCOPED INVITE SENT BY CLAUDE", true, false)]
    [InlineData("FOLLOW-UP MESSAGE SENT by Claude", false, true)]
    [InlineData("SECOND FOLLOW-UP MESSAGE SENT BY CLAUDE", false, true)]
    [InlineData("INBOUND InMail 7:01 PM", false, false)]
    [InlineData("CONNECTION ACCEPTED 10/20", false, false)]
    [InlineData("NOT APPLIED, DROPPED by bg1020b", false, false)]
    [InlineData("Connect note SENT 10/26 8:40 AM CDT", true, false)]
    [InlineData("REJECTION, generic", false, false)]
    [InlineData("", false, false)]
    public void Notes_decide_the_kind_of_a_LinkedIn_row(string notes, bool invite, bool message)
    {
        string[] row = ["1", "Pat Lee", "Acme", "LinkedIn", "11/2/2026", "", notes];
        Assert.Equal(invite, TrackerKinds.IsInvite(row));
        Assert.Equal(message, TrackerKinds.IsMessage(row));
    }

    [Fact]
    public void An_Easy_Apply_row_is_no_LinkedIn_action()
    {
        string[] row = ["1", "NA", "Acme", "LinkedIn Easy Apply", "11/2/2026", "", "Application submitted"];
        Assert.False(TrackerKinds.IsLinkedIn(row));
        Assert.False(TrackerKinds.IsMessage(row));
    }

    [Fact]
    public void A_row_with_no_date_counts_for_nothing()
    {
        Assert.Equal(new OutreachCounts(0, 0, 0, 0), OutreachCounts.From([["1", "Pat Lee", "Acme", "LinkedIn", "soon", "", "CONNECTION REQUEST SENT"]], Today));
    }

    [Fact]
    public void An_invite_eight_days_old_has_left_the_rolling_week()
    {
        string[] old = ["1", "Pat Lee", "Acme", "LinkedIn", "10/25/2026", "", "CONNECTION REQUEST SENT"];
        string[] edge = ["2", "Sam Roe", "Acme", "LinkedIn", "10/27/2026", "", "CONNECTION REQUEST SENT"];
        Assert.Equal(1, OutreachCounts.From([old, edge], Today).InvitesWeek);
    }

    private static PersonGate GateOver(params string[][] rows) =>
        new(new GateSources(rows, Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), []), Today);

    [Fact]
    public void A_sent_message_whose_body_says_applied_is_still_a_message()
    {
        string[] row = ["2724", "New Person", "Acme", "LinkedIn", "10/14/2026", "", "SENT BY CLAUDE (bg1014-web): I applied on Monday for the role."];
        Assert.True(TrackerKinds.IsMessage(row));
        Assert.Equal("Rule 25: already messaged", GateOver(row).Message("New Person", "Acme"));
    }

    [Fact]
    public void Rule_25_reads_the_person_on_any_row_whatever_the_company_cell_says()
    {
        string[] elsewhere = ["1", "New Person", "(company not on profile line)", "LinkedIn", "10/14/2026", "", "SENT by Claude."];
        string[] titled = ["2", "Dr. New Person", "Acme", "LinkedIn", "10/14/2026", "", "SENT by Claude."];
        string[] unnamed = ["3", "NA", "Intelex", "LinkedIn", "10/14/2026", "", "SENT by Claude: pre-interview message to New Person."];
        Assert.Equal("Rule 25: already messaged", GateOver(elsewhere).Message("New Person", "Acme"));
        Assert.Equal("Rule 25: already messaged", GateOver(titled).Message("New Person", "Acme"));
        Assert.Equal("Rule 25: already messaged", GateOver(unnamed).Message("New Person", "Acme"));
    }

    [Theory]
    [InlineData("New Person, Jr.")]
    [InlineData("New (Xinda) Person")]
    [InlineData("New R. Person, CFP, AIF")]
    public void A_suffix_or_a_middle_name_is_the_same_person(string cell)
    {
        string[] row = ["1", cell, "Acme", "LinkedIn", "10/14/2026", "", "FOLLOW-UP MESSAGE SENT by Claude."];
        Assert.Equal("Rule 25: already messaged", GateOver(row).Message("New Person", "Acme"));
        Assert.Equal("Acme", GateOver(row).CompanyOf("New Person"));
        Assert.Equal(string.Empty, GateOver(row).CompanyOf("Someone Else"));
    }

    [Fact]
    public void An_unnamed_invite_is_out_when_the_company_was_invited_this_month()
    {
        string[] row = ["1", "Sam Roe", "ServiceTitan", "LinkedIn", "10/30/2026", "", "CONNECTION REQUEST SENT by Claude"];
        Assert.Equal("already invited someone there within 30 days", GateOver(row).Invite(string.Empty, "ServiceTitan"));
        Assert.Null(GateOver(row).Invite(string.Empty, "Other Co"));
    }

    [Fact]
    public void A_lane_picking_a_name_is_told_who_is_already_on_the_tracker()
    {
        string[] row = ["1", "Sam Roe", "ServiceTitan", "LinkedIn", "8/30/2026", "", "SECOND FOLLOW-UP MESSAGE SENT BY CLAUDE"];
        string[] blank = ["2", "NA", "ServiceTitan", "LinkedIn", "8/30/2026", "", "SENT"];
        Assert.Equal(["Sam Roe"], GateOver(row, blank).KnownAt("ServiceTitan"));
    }

    [Fact]
    public void A_posting_applied_to_is_found_by_its_id_or_its_title()
    {
        string[] byId = ["1", "NA", "Acme", "LinkedIn Easy Apply", "10/14/2026", "", "SUBMITTED by Claude, job 4467083755."];
        string[] byTitle = ["2", "NA", "Acme", "Greenhouse", "10/14/2026", "", "Application submitted, Staff Engineer, Platform."];
        Assert.Equal("already applied", GateOver(byId).Apply("Acme", "https://www.linkedin.com/jobs/view/4467083755/", "Other"));
        Assert.Equal("already applied", GateOver(byTitle).Apply("Acme", "https://jobs.lever.co/acme/abc", "Staff Engineer, Platform"));
        Assert.Null(GateOver(byTitle).Apply("Acme", "https://jobs.lever.co/acme/abc", "Principal Engineer"));
    }

    [Fact]
    public void Company_blocks_come_from_their_table()
    {
        Assert.Equal(["ELEKS"], PersonGate.CompanyBlocks(Fixture.Read("limits.md")));
    }

    [Fact]
    public void A_blocked_employer_is_out_on_sight()
    {
        Assert.Equal("company block", Gate().Invite("Pat Lee", "ELEKS"));
        Assert.Equal("company block", Gate().Message("New Person", "Eleks"));
    }

    [Fact]
    public void Rule_25_takes_a_messaged_person_off_the_message_list()
    {
        Assert.Equal("Rule 25: already messaged", Gate().Message("Sam Roe", "LINQ"));
        Assert.Null(Gate().Message("New Person", "LINQ"));
    }

    [Fact]
    public void An_invited_person_gets_no_second_invite()
    {
        Assert.Equal("already invited", Gate().Invite("Ann Example", "AccuLynx"));
    }

    [Fact]
    public void Former_employers_and_agencies_are_out()
    {
        Assert.Equal("former employer: Conway Investment Research", Gate().Invite("New Person", "Conway"));
        Assert.Equal("agency-submitted", Gate("Talently").Invite("New Person", "Talently"));
    }

    [Fact]
    public void Nobody_in_the_no_template_tables_gets_a_template()
    {
        var names = PersonGate.NoTemplate(Fixture.Read("responders.md"));
        Assert.Contains("Pat Lee", names);
        Assert.Contains("Jo Ray", names);
        Assert.DoesNotContain("Bo Example", names);
        Assert.Equal("responders.md: no template", Gate().Invite("Jo Ray", "Other Co"));
    }

    [Fact]
    public void A_company_with_a_process_running_gets_no_cold_template()
    {
        Assert.Equal("live process at the company", Gate().Invite("New Person", "Acme Health"));
    }

    [Fact]
    public void Missing_sections_stop_the_gate_rather_than_open_it()
    {
        Assert.Throws<FormatException>(() => PersonGate.CompanyBlocks("nothing"));
        Assert.Throws<FormatException>(() => PersonGate.NoTemplate("nothing"));
    }

    [Fact]
    public void Rule_50_and_the_do_not_apply_list_hold_applications()
    {
        Assert.Contains("do-not-apply list", Gate().Apply("Upstart Network", "https://x", "Role"), StringComparison.Ordinal);
        Assert.Null(Gate().Apply("New Co", "https://job-boards.greenhouse.io/newco/jobs/1", "Role"));
    }

    [Fact]
    public void A_posting_already_on_a_row_and_a_staffing_firm_are_out()
    {
        Assert.Equal("already applied", Gate().Apply("Acme Network", "https://job-boards.greenhouse.io/acmenetwork/jobs/123456", "Role"));
        Assert.Equal("skipped: staffing firm", Gate().Apply("Steneral Consulting", "https://x", "Role"));
    }

    [Fact]
    public void Three_applications_in_the_window_hold_the_fourth_whatever_the_list_says()
    {
        var rows = new List<string[]>
        {
            new[] { "1", "NA", "Acme", "Greenhouse", "10/1/2026", "", "Application submitted" },
            new[] { "2", "NA", "Acme", "Greenhouse", "10/2/2026", "", "Application submitted" },
            new[] { "3", "NA", "Acme", "Greenhouse", "10/3/2026", "", "Application submitted" },
        };
        var gate = new PersonGate(new GateSources(rows, Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), []), Today);
        Assert.StartsWith("Rule 50:", gate.Apply("Acme", "https://x", "Role"), StringComparison.Ordinal);
    }
}
