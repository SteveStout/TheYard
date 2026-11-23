namespace JobHunt.Server.Tests;

/// <summary>
/// The apply lane's memory of the forms it skipped: a posting it read and would not submit stays off the next mornings'
/// lists for a week, so the day's few rows go to postings it has not read, and a skip that was only the run's own trouble
/// is not kept.
/// </summary>
public sealed class ApplySkipsTests
{
    private const string Folder = "nightly/2026-11-03/0600-apply-sonnet-5-5";

    private const string DentalLink = "https://job-boards.greenhouse.io/acmedental/jobs/6222461004";

    private static ClaudeRun Quiet(ClaudeRequest request) => new(0, false, ResultLine.Empty);

    private static ClaudeRun Skipped(string reason) =>
        new(0, false, ResultLine.With($$"""{"rows":[{"row":1,"outcome":"skipped","reason":"{{reason}}","person":null,"title":null,"profile_url":null,"evidence":null}],"live_invite_count":null,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));

    private static async Task<Morning> RunAsync(Func<ClaudeRequest, ClaudeRun> script, Action<Morning>? seed = null, bool dry = false)
    {
        var morning = new Morning(script);
        seed?.Invoke(morning);
        await morning.Lanes().ExecuteAsync(new RunOptions(dry, Lane.Apply), default);
        return morning;
    }

    [Fact]
    public void A_skip_is_a_line_of_address_day_and_reason()
    {
        Assert.Equal($"{DentalLink}\t2026-11-03\tsigned page: an arbitration agreement\n", ApplySkips.Line(DentalLink, "signed page:  an arbitration\tagreement", new DateOnly(2026, 11, 3)));
    }

    [Theory]
    [InlineData("Run ended: browser_down. Not opened.")]
    [InlineData("Renderer froze twice right after the Connect click")]
    [InlineData("the page timed out")]
    [InlineData("the tab group changed mid-run")]
    [InlineData("")]
    public void A_skip_that_was_the_runs_own_trouble_is_not_kept(string reason)
    {
        Assert.Null(ApplySkips.Line(DentalLink, reason, new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void A_row_with_no_address_is_not_kept()
    {
        Assert.Null(ApplySkips.Line("  ", "no banked answer", new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void A_skip_holds_for_a_week_and_never_from_the_future()
    {
        string table = $"{DentalLink}\t2026-11-03\tno banked answer\nhttps://x.example/old\t2026-10-27\tgone\nhttps://x.example/edge\t2026-10-28\tgone\nhttps://x.example/ahead\t2026-11-09\tgone\nnot a line\n";
        var kept = ApplySkips.Remembered(table, new DateOnly(2026, 11, 3));
        Assert.Equal(["https://job-boards.greenhouse.io/acmedental/jobs/6222461004", "https://x.example/edge"], kept.Order(StringComparer.Ordinal));
        Assert.Contains("https://x.example/edge", ApplySkips.Remembered(table, new DateOnly(2026, 11, 3)));
        Assert.DoesNotContain("https://x.example/edge", ApplySkips.Remembered(table, new DateOnly(2026, 11, 4)));
    }

    [Theory]
    [InlineData("https://Job-Boards.Greenhouse.io/AcmeDental/jobs/6222461004/")]
    [InlineData("https://job-boards.greenhouse.io/acmedental/jobs/6222461004?gh_src=abc#apply")]
    [InlineData("  https://job-boards.greenhouse.io/acmedental/jobs/6222461004  ")]
    public void One_posting_is_one_key_however_the_address_is_written(string link)
    {
        Assert.Equal(ApplySkips.Key(DentalLink), ApplySkips.Key(link));
    }

    [Fact]
    public void An_ashby_form_and_its_posting_page_are_one_key()
    {
        Assert.Equal(ApplySkips.Key("https://jobs.ashbyhq.com/homebound/b0761174"), ApplySkips.Key("https://jobs.ashbyhq.com/Homebound/b0761174/application"));
        Assert.NotEqual(ApplySkips.Key("https://jobs.ashbyhq.com/homebound/b0761174"), ApplySkips.Key("https://jobs.ashbyhq.com/homebound/b0761175/application"));
    }

    [Fact]
    public async Task A_form_the_lane_skipped_is_written_to_the_table_and_named_in_the_note()
    {
        Morning morning = await RunAsync(_ => Skipped("onsite question: relocate to Dallas, yes or no"));
        string table = morning.Share.Files[WritePolicy.SkipsTable];
        Assert.Contains($"{DentalLink}\t2026-11-03\tonsite question: relocate to Dallas, yes or no\n", table, StringComparison.Ordinal);
        Assert.Contains("1 skipped forms kept off the list for 7 days", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_skip_that_was_the_runs_trouble_writes_no_table()
    {
        Morning morning = await RunAsync(_ => Skipped("Run ended: browser_down. Not opened."));
        Assert.False(morning.Share.Files.ContainsKey(WritePolicy.SkipsTable));
    }

    [Fact]
    public async Task A_sent_row_and_a_failed_row_are_not_kept()
    {
        Morning morning = await RunAsync(_ => new ClaudeRun(0, false, ResultLine.With("""{"rows":[{"row":1,"outcome":"failed","reason":"the Submit click did nothing","person":null,"title":null,"profile_url":null,"evidence":null}],"live_invite_count":null,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}""")));
        Assert.False(morning.Share.Files.ContainsKey(WritePolicy.SkipsTable));
    }

    [Fact]
    public async Task A_dry_run_keeps_nothing()
    {
        Morning morning = await RunAsync(_ => Skipped("no banked answer"), dry: true);
        Assert.False(morning.Share.Files.ContainsKey(WritePolicy.SkipsTable));
    }

    [Fact]
    public async Task A_posting_skipped_this_week_is_off_the_list_and_counted_once()
    {
        Morning morning = await RunAsync(Quiet, seed: m => m.Share.Files[WritePolicy.SkipsTable] = $"{DentalLink}\t2026-11-02\tno banked answer\n");
        Assert.DoesNotContain("Acme Dental", morning.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);
        Assert.Contains("1 postings: off the list before the gates: the lane read their forms and skipped them in the last 7 days", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_next_row_takes_the_place_of_a_remembered_skip()
    {
        Morning fresh = await RunAsync(Quiet);
        string[] before = fresh.Share.Files[$"{Folder}/worklist.tsv"].TrimEnd().Split('\n');
        Morning held = await RunAsync(Quiet, seed: m => m.Share.Files[WritePolicy.SkipsTable] = $"{DentalLink}\t2026-11-02\tno banked answer\n");
        string[] after = held.Share.Files[$"{Folder}/worklist.tsv"].TrimEnd().Split('\n');
        Assert.Contains("Acme Dental", before[1], StringComparison.Ordinal);
        Assert.Equal(before.Length - 1, after.Length);
        Assert.Equal(before[2].Split('\t')[2], after[1].Split('\t')[2]);
    }

    [Fact]
    public async Task A_skip_older_than_a_week_is_tried_again()
    {
        Morning morning = await RunAsync(Quiet, seed: m => m.Share.Files[WritePolicy.SkipsTable] = $"{DentalLink}\t2026-10-27\tno banked answer\n");
        Assert.Contains("Acme Dental", morning.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_on_the_reopen_list_is_tried_again_whatever_the_table_says()
    {
        Morning morning = await RunAsync(Quiet, seed: m =>
        {
            m.Share.Files[WritePolicy.SkipsTable] = $"{DentalLink}\t2026-11-02\tno banked answer\n";
            m.Share.Files["nightly/apply-reopen.tsv"] = $"url\twhy\tadded_by\tadded_on\n{DentalLink}/\tSteve banked the answer\tsteve\t2026-11-03\n";
        });
        Assert.Contains("Acme Dental", morning.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);
    }

    // heal-server6am-apply-20261123: the 6 AM apply lane closed 11/20 and 11/23 with no form sent. 1Password was skipped
    // four mornings for the same unbanked "cybersecurity SaaS" question, Esri three for the same redirect to esri.com and
    // Anduril twice for its export-control questions, each time under an address the table had not seen, because the
    // memory held one address and the company's next posting took the row. The fixture is J:\dispatch\heal-fixtures\
    // 30-heal-server6am-apply-20261123.txt; the addresses and reasons are the 11/20 table's lines and the 11/23 worklist's.
    private const string HealSweep = "APPLY-SWEEP-2026-11-02-heal.md";

    private const string HealSkips =
        "https://jobs.ashbyhq.com/1password/8ff17ade-462e-4afe-9255-b4193d0b8424/application\t2026-11-01\tno banked answer: cybersecurity SaaS experience, salary range as numbers, three required essays\n"
        + "https://job-boards.greenhouse.io/esri/jobs/5291879007\t2026-11-01\tcompany site esri.com, not a no-account board: the Greenhouse URL redirected to an esri.com careers page\n"
        + "https://job-boards.greenhouse.io/andurilindustries/jobs/5277698007\t2026-11-01\tno banked answer: export-controls and government-oversight questions\n"
        + "https://job-boards.greenhouse.io/axon/jobs/7997878003\t2026-11-01\tsignature page: Prohibited Possessor Questionnaire acknowledgement\n"
        + "https://job-boards.greenhouse.io/rocketlab/jobs/8202565003\t2026-11-01\tonsite question: willing to work onsite in Littleton, CO\n"
        + "https://job-boards.greenhouse.io/conga/jobs/5292530007\t2026-11-01\tgone or closed: the URL redirects to the Conga board index\n"
        + "https://www.linkedin.com/jobs/view/4491233145/\t2026-11-01\tclosed: LinkedIn shows no longer accepting applications\n";

    private static string HealSweepFile(params (string Company, string Role, string Link)[] rows) =>
        "# APPLY SWEEP 2026-11-02, heal-server6am-apply-20261123\n\n### CONFIRMED LIVE on the employer's own board\n| # | Company | Role | Board | Link |\n|---|---|---|---|---|\n"
        + string.Concat(rows.Select((row, i) => $"| {i + 1} | {row.Company} | {row.Role} | NO account | {row.Link} |\n"));

    private static readonly (string Company, string Role, string Link)[] TheMorningOf1123 =
    [
        ("Anduril Industries", "Senior Mission Planning Engineer", "https://job-boards.greenhouse.io/andurilindustries/jobs/5303810007"),
        ("1Password", "Engineering Manager, Infrastructure (Device Trust)", "https://jobs.ashbyhq.com/1password/f0100f8b-ca22-4cf3-abd8-fda500de8d9e"),
        ("Esri", "Principal Software Development Engineer - National Imagery", "https://job-boards.greenhouse.io/esri/jobs/5218015007"),
        ("Rocket Lab Corporation", "Staff Software Engineer, Flight Software", "https://job-boards.greenhouse.io/rocketlab/jobs/8202565999"),
        ("Conga", "Principal Software Engineer", "https://job-boards.greenhouse.io/conga/jobs/5292530999"),
    ];

    [Fact]
    public void Only_a_skip_for_the_company_s_own_reason_holds_its_board()
    {
        Assert.Equal(["ashby/1password", "greenhouse/andurilindustries", "greenhouse/axon", "greenhouse/esri"], ApplySkips.Boards(HealSkips, new DateOnly(2026, 11, 3)).Order(StringComparer.Ordinal));
        Assert.Empty(ApplySkips.Boards(HealSkips, new DateOnly(2026, 11, 8)));
        Assert.Empty(ApplySkips.Boards("https://job-boards.greenhouse.io/esri/jobs/1\t2026-11-02\n", new DateOnly(2026, 11, 3)));
    }

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/andurilindustries/jobs/5277698007", "greenhouse/andurilindustries")]
    [InlineData("https://boards.greenhouse.io/andurilindustries/jobs/5303391007?gh_jid=5303391007", "greenhouse/andurilindustries")]
    [InlineData("https://jobs.ashbyhq.com/M-KOPA/ebab596b-350b-45bc-8c53-7d1943fefaae", "ashby/m-kopa")]
    [InlineData("  https://jobs.lever.co/acme/1234/apply", "lever/acme")]
    [InlineData("https://www.linkedin.com/jobs/view/4491233145/", null)]
    [InlineData("https://boards.greenhouse.io/embed/job_app?for=esri&token=1", null)]
    [InlineData("https://www.esri.com/careers/5291772007", null)]
    public void A_posting_s_board_is_its_host_and_company_token(string link, string? board) => Assert.Equal(board, ApplySkips.Board(link));

    [Fact]
    public async Task The_fixture_a_company_whose_form_stopped_the_lane_does_not_take_a_row_again_under_another_posting()
    {
        Morning morning = await RunAsync(Quiet, seed: m =>
        {
            m.Share.Files[HealSweep] = HealSweepFile(TheMorningOf1123);
            m.Share.Files[WritePolicy.SkipsTable] = HealSkips;
        });
        string list = morning.Share.Files[$"{Folder}/worklist.tsv"];
        Assert.DoesNotContain("1Password", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Esri", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Anduril Industries", list, StringComparison.Ordinal);
        Assert.Contains("3 postings: off the list before the gates: another of the company's forms stopped the lane in the last 7 days", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_skip_about_one_posting_holds_only_that_posting_and_the_company_s_others_still_take_rows()
    {
        Morning morning = await RunAsync(Quiet, seed: m =>
        {
            m.Share.Files[HealSweep] = HealSweepFile(TheMorningOf1123);
            m.Share.Files[WritePolicy.SkipsTable] = HealSkips;
        });
        string list = morning.Share.Files[$"{Folder}/worklist.tsv"];
        Assert.Contains("rocketlab/jobs/8202565999", list, StringComparison.Ordinal);
        Assert.Contains("conga/jobs/5292530999", list, StringComparison.Ordinal);
        Assert.Contains("Acme Dental", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_company_s_posting_on_the_reopen_list_is_tried_again_whatever_its_other_forms_said()
    {
        Morning morning = await RunAsync(Quiet, seed: m =>
        {
            m.Share.Files[HealSweep] = HealSweepFile(TheMorningOf1123);
            m.Share.Files[WritePolicy.SkipsTable] = HealSkips;
            m.Share.Files["nightly/apply-reopen.tsv"] = "url\twhy\tadded_by\tadded_on\nhttps://jobs.ashbyhq.com/1password/f0100f8b-ca22-4cf3-abd8-fda500de8d9e\tSteve banked the answer\tsteve\t2026-11-03\n";
        });
        string list = morning.Share.Files[$"{Folder}/worklist.tsv"];
        Assert.Contains("1Password", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Esri", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_company_hold_ends_with_its_week()
    {
        Morning morning = await RunAsync(Quiet, seed: m =>
        {
            m.Share.Files[HealSweep] = HealSweepFile(TheMorningOf1123);
            m.Share.Files[WritePolicy.SkipsTable] = HealSkips.Replace("2026-11-01", "2026-10-27", StringComparison.Ordinal);
        });
        Assert.Contains("Esri", morning.Share.Files[$"{Folder}/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_table_is_a_file_the_program_may_write_and_no_other_one_beside_it_is()
    {
        Assert.True(WritePolicy.Allowed("nightly/apply-skipped.tsv"));
        Assert.False(WritePolicy.Allowed("nightly/apply-skipped.tsv.bak"));
        Assert.False(WritePolicy.Allowed("nightly/apply-reopen.tsv"));
    }
}
