using JobHunt.Functions.Merge;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>The merger's rules, each on a scratch folder: rows, fixes, moveouts and the guards around them.</summary>
public sealed class TrackerMergerTests
{
    [Fact]
    public async Task A_new_row_takes_the_next_Count_above_both_files_and_carries_its_receipt()
    {
        using var s = new Scratch();
        s.Master("10,Ann,Acme,Email,10/1/2026,,first\r\n");
        s.MovedOut("12,Bob,Beta,Email,10/2/2026,,moved\r\n");
        s.Spool("20261022-150000Z-lane-a.csv", "Person,Company,Communication,Date,Phone/Email,Notes\r\nCy,\"Gamma, Inc\",LinkedIn,10/22/2026,,\"said \"\"hi\"\"\nand left\"\r\n");

        await s.RunAsync();

        List<string[]> rows = TrackerCsvText.Parse(s.Text(MergePaths.Master));
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "13", "Cy", "Gamma, Inc", "LinkedIn", "10/22/2026", "", "said \"hi\"\nand left [TID:20261022-150000Z-lane-a]" }, rows[2]);
        Assert.True(s.Has("tracker-inbox/merged/20261022-150000Z-lane-a.csv"));
        Assert.Contains("MERGED 20261022-150000Z-lane-a.csv -> Count 13 (1 row(s))", s.Text(MergePaths.Log));
    }

    [Fact]
    public async Task A_seven_column_row_has_its_Count_ignored()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Spool("20261022-150100Z-lane-b.csv", "999,Dee,Delta,Email,10/22/2026,dee@x.com,note\r\nEd,Echo,Phone,10/22/2026,,\r\n");

        await s.RunAsync();

        List<string[]> rows = TrackerCsvText.Parse(s.Text(MergePaths.Master));
        Assert.Equal("6", rows[2][0]);
        Assert.Equal("Dee", rows[2][1]);
        Assert.Equal("7", rows[3][0]);
        Assert.Equal("[TID:20261022-150100Z-lane-b]", rows[3][6]);
    }

    [Fact]
    public async Task A_row_already_moved_out_is_not_merged_again()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.MovedOut("6,Dee,Delta,Email,10/22/2026,,gone [TID:20261022-150100Z-lane-b] [MOVED:moveout-x]\r\n");
        s.Spool("20261022-150100Z-lane-b.csv", "Dee,Delta,Email,10/22/2026,,gone\r\n");
        byte[] before = s.Bytes(MergePaths.Master);

        await s.RunAsync();

        Assert.Equal(before, s.Bytes(MergePaths.Master));
        Assert.True(s.Has("tracker-inbox/merged/20261022-150100Z-lane-b.csv"));
    }

    [Fact]
    public async Task A_file_with_no_data_rows_is_rejected_and_the_master_is_untouched()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Spool("20261022-150200Z-lane-c.csv", "Person,Company,Communication,Date,Phone/Email,Notes\r\n");
        byte[] before = s.Bytes(MergePaths.Master);

        await s.RunAsync();

        Assert.Equal(before, s.Bytes(MergePaths.Master));
        Assert.True(s.Has("tracker-inbox/rejected/20261022-150200Z-lane-c.csv"));
    }

    [Fact]
    public async Task A_fix_replaces_its_row_in_place_carries_old_receipts_and_keeps_the_byte_order_mark()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x [TID:t1]\r\n6,Bob,Beta,Narrative text,10/2/2026,,y [TID:t2]\r\n", bom: true);
        s.Spool("fix-6-lane-shape.csv", "6,Bob,Beta,Email,10/2/2026,,Narrative text y\r\n");

        await s.RunAsync();

        byte[] bytes = s.Bytes(MergePaths.Master);
        Assert.True(TrackerCsvText.HasBom(bytes));
        List<string[]> rows = TrackerCsvText.Parse(TrackerCsvText.Decode(bytes));
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "6", "Bob", "Beta", "Email", "10/2/2026", "", "Narrative text y [FIX:fix-6-lane-shape] [TID:t2]" }, rows[2]);
        Assert.Equal("x [TID:t1]", rows[1][6]);
        Assert.True(s.Has("tracker-inbox/merged/fix-6-lane-shape.csv"));
    }

    [Fact]
    public async Task A_fix_for_a_Count_that_is_not_there_is_rejected()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Spool("fix-9-lane-x.csv", "9,Bob,Beta,Email,10/2/2026,,y\r\n");

        await s.RunAsync();

        Assert.True(s.Has("tracker-inbox/rejected/fix-9-lane-x.csv"));
    }

    [Fact]
    public async Task A_moveout_without_Steves_approval_line_is_refused_untouched()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Spool("moveout-lane-x.txt", "5\r\n");
        byte[] before = s.Bytes(MergePaths.Master);

        await s.RunAsync();

        Assert.Equal(before, s.Bytes(MergePaths.Master));
        Assert.False(s.Has(MergePaths.MovedOut));
        Assert.True(s.Has("tracker-inbox/rejected/moveout-lane-x.txt"));
    }

    [Fact]
    public async Task A_moveout_copies_the_rows_out_first_then_removes_them_from_the_master()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n6,Bob,Beta,Email,10/2/2026,,y\r\n7,Cy,Gamma,Email,10/3/2026,,z\r\n");
        s.Spool("moveout-lane-y.txt", "# approved-by: Steve 10/22/2026 move these\r\n5, 7\r\n");

        await s.RunAsync();

        List<string[]> master = TrackerCsvText.Parse(s.Text(MergePaths.Master));
        Assert.Equal(new[] { "Count", "6" }, master.Select(r => r[0]));
        List<string[]> moved = TrackerCsvText.Parse(s.Text(MergePaths.MovedOut));
        Assert.Equal(new[] { "Count", "5", "7" }, moved.Select(r => r[0]));
        Assert.Equal("x [MOVED:moveout-lane-y]", moved[1][6]);
        Assert.True(s.Has("tracker-inbox/merged/moveout-lane-y.txt"));
    }

    [Fact]
    public async Task Cloud_lane_files_in_inbox_tracker_inbox_merge_and_stay_in_their_own_folder()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Write("inbox/tracker-inbox/20261022-170000Z-bookkeeping-ats.csv", "Dee,Delta,Greenhouse,10/22/2026,,receipt\r\n");

        int waiting = await s.RunAsync();

        Assert.Equal(1, waiting);
        Assert.Equal("6", TrackerCsvText.Parse(s.Text(MergePaths.Master))[2][0]);
        Assert.True(s.Has("inbox/tracker-inbox/merged/20261022-170000Z-bookkeeping-ats.csv"));
    }

    [Fact]
    public async Task A_second_copy_of_a_merged_file_is_filed_as_a_duplicate_without_a_second_row()
    {
        using var s = new Scratch();
        s.Master("5,Ann,Acme,Email,10/1/2026,,x\r\n");
        s.Spool("20261022-150000Z-lane-a.csv", "Dee,Delta,Email,10/22/2026,,y\r\n");
        await s.RunAsync();
        s.Spool("20261022-150000Z-lane-a.csv", "Dee,Delta,Email,10/22/2026,,y\r\n");

        await s.RunAsync();

        Assert.Equal(3, TrackerCsvText.Parse(s.Text(MergePaths.Master)).Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(s.Root, "tracker-inbox", "merged")).Length);
    }
}
