namespace JobHunt.Server.Tests;

/// <summary>
/// Item 2 of jhfix1119: the apply list refills from the picker's passed rows of the last three days; and pouncetitle1120:
/// the one title test (RoleTitle) holds both that refill and every READY row, so no people, product, sales or business
/// development seat reaches the list or APPLY-BLOCKERS.
/// </summary>
public sealed class FreshPostingsTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private const string Head = "# scanned 12281 postings from 143 boards, 24 alert leads; 48 passed the gates\n"
        + "Source\tCompany\tTitle\tLevel\tUrl\tBoardKind\tPosted\tDaysOld\tBand\tBandFlag\tRemote\tConfirmed\tStack\tTrackerNote\tWhyPassed\tHold\tForm\n";

    private static string Row(string company, string title, string posted, string confirmed = "feed", string hold = "") =>
        $"feed:ashby\t{company}\t{title}\tStaff\thttps://jobs.ashbyhq.com/{company.ToLowerInvariant()}/1\tashby\t{posted}\t1\t\tNONE\tremote\t{confirmed}\tdotnet\tno applications in 60 days\tremote\t{hold}\t\n";

    private static readonly string Tsv = Head
        + Row("Northwind", "Staff Software Engineer", "2026-11-18")
        + Row("Weave", "Product Manager, Phones Platform", "2026-11-18")
        + Row("Ripple", "Business Development Senior Manager", "2026-11-18")
        + Row("Contoso", "Principal Engineer", "2026-11-10")
        + Row("Fabrikam", "Staff Engineer", "2026-11-17", confirmed: "alert")
        + Row("Headway", "Senior Engineering Manager (Patient New Verticals)", "2026-11-18", hold: "headline: 50 characters")
        + "# FAILED\n"
        + Row("Initech", "Staff Engineer", "2026-11-18");

    [Fact]
    public void Only_fresh_feed_rows_with_no_hold_and_a_seat_title_above_the_failed_line_are_read()
    {
        var postings = FreshPostings.Read("nightly/jobs-candidates-2026-11-19.tsv", Tsv, Today);
        var only = Assert.Single(postings);
        Assert.Equal(("Northwind", "Staff Software Engineer", "2026-11-18"), (only.Company, only.Role, only.Posted));
        Assert.Equal("nightly/jobs-candidates-2026-11-19.tsv:3", only.Source);
    }

    [Fact]
    public void A_file_with_no_header_reads_as_nothing() => Assert.Empty(FreshPostings.Read("x", "# nothing\n", Today));

    [Fact]
    public async Task A_fresh_picker_row_reaches_the_apply_worklist_after_the_ready_rows()
    {
        var morning = new Morning(_ => throw new InvalidOperationException("no lane runs in this test"));
        morning.Share.Files[FreshPostings.PathFor(DateOnly.FromDateTime(Morning.At.UtcDateTime))] = Tsv.Replace("2026-11-18", "2026-11-02", StringComparison.Ordinal);
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        var reads = await new DayReader(morning.Share).ReadAsync(DateOnly.FromDateTime(Morning.At.UtcDateTime), default);

        var list = await builder.ApplyAsync(reads, null, false, default);

        Assert.Contains(list.Rows, row => row.Company == "Northwind" && row.Source.StartsWith("nightly/jobs-candidates-2026-11-03.tsv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ready_row_the_title_test_fails_is_dropped_with_its_word_and_never_reaches_the_blockers()
    {
        var morning = new Morning(_ => throw new InvalidOperationException("no lane runs in this test"));
        morning.Share.Files["JOBS-2026-11-02.md"] = "## READY TO APPLY\n| # | Company | Role | Link | Board | Band | Posted | Notes |\n|---|---|---|---|---|---|---|---|\n"
            + "| 1 | ASM | Director, Business Development | https://asm.wd3.myworkdayjobs.com/asm/job/1 | Workday, ACCOUNT WALL | PASS | 2026-11-02 | |\n"
            + "| 2 | Delinea | Director, People Business Partner - Product, Technology & AI | https://jobs.ashbyhq.com/delinea/83c3a7e2-cac4-444b-a66d-c1153d15b4a5 | ashby | PASS | 2026-11-02 | |\n"
            + "| 3 | Contoso | Staff Engineer | https://jobs.lever.co/contoso/b | lever | PASS | 2026-11-02 | |\n";
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        var reads = await new DayReader(morning.Share).ReadAsync(DateOnly.FromDateTime(Morning.At.UtcDateTime), default);

        var list = await builder.ApplyAsync(reads, null, false, default);

        Assert.Equal("not an engineering role: Business Development", Assert.Single(list.Dropped, d => d.What == "ASM, Director, Business Development").Reason);
        Assert.Equal("not an engineering role: People Business Partner", Assert.Single(list.Dropped, d => d.What.StartsWith("Delinea", StringComparison.Ordinal)).Reason);
        Assert.DoesNotContain(list.Blockers, posting => posting.Company is "ASM" or "Delinea");
        Assert.DoesNotContain(list.Rows, row => row.Company is "ASM" or "Delinea");
        Assert.Contains(list.Rows, row => row.Company == "Contoso");
    }
}
