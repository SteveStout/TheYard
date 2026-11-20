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

    [Fact]
    public void The_table_is_a_file_the_program_may_write_and_no_other_one_beside_it_is()
    {
        Assert.True(WritePolicy.Allowed("nightly/apply-skipped.tsv"));
        Assert.False(WritePolicy.Allowed("nightly/apply-skipped.tsv.bak"));
        Assert.False(WritePolicy.Allowed("nightly/apply-reopen.tsv"));
    }
}
