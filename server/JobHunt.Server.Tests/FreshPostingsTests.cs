namespace JobHunt.Server.Tests;

/// <summary>Item 2 of jhfix1119: the apply list refills from the picker's passed rows of the last three days.</summary>
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
}
