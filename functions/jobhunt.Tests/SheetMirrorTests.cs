using System.Reflection;
using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The Sheet mirror (functions/JobHunt.Application/Features/Sheet): which master rows the Sheet lacks and may take, the
/// stops the records run applied by hand (drift, a broken numbering, a banned row, a sentence in
/// Communication), the read-back after an append, and the red lines as tests: the grid port has
/// two methods, no code under Sheet/ can edit, clear or delete, and shadow adds nothing.
/// Every row here is made up; no row of the real tracker is in this repository.
/// </summary>
public sealed class SheetMirrorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 17, 0, 0, TimeSpan.Zero);

    private static string[] Row(int count, string person = "Ann Example", string company = "Acme", string comm = "Email", string notes = "Application submitted through the careers page") =>
        [count.ToString(System.Globalization.CultureInfo.InvariantCulture), person, company, comm, "2026-10-22", "ann@acme.example", notes];

    private static List<IReadOnlyList<string>> Grid(params string[][] masterRows)
    {
        var grid = new List<IReadOnlyList<string>> { new[] { "Count", "Person", "Company", "Communication", "Date", "Phone/Email", "Notes" } };
        for (int i = 0; i < masterRows.Length; i++)
        {
            grid.Add([(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), .. masterRows[i][1..]]);
        }
        return grid;
    }

    [Fact]
    public void The_rows_the_sheet_lacks_are_added_in_count_order_numbered_from_the_last_display_number()
    {
        // Count 12 was moved out, so the master's Counts skip it and the display numbers do not.
        var master = new List<string[]> { Row(10), Row(11), Row(13), Row(14, "Bo Example"), Row(15, "Cy Example") };
        var plan = SheetPlan.Plan([master[4], master[2], master[0], master[3], master[1]], Grid(master[0], master[1], master[2]));
        Assert.Null(plan.Reason);
        Assert.Equal(["14", "15"], plan.Counts);
        Assert.Equal(["4", "Bo Example", "Acme", "Email", "2026-10-22", "ann@acme.example", "Application submitted through the careers page"], plan.Appends[0]);
        Assert.Equal("5", plan.Appends[1][0]);
        Assert.True(plan.InStep);
    }

    [Fact]
    public void A_sheet_in_step_with_the_master_plans_nothing()
    {
        var master = new List<string[]> { Row(1), Row(2) };
        var plan = SheetPlan.Plan(master, Grid([.. master]));
        Assert.Empty(plan.Appends);
        Assert.Null(plan.Reason);
    }

    [Fact]
    public void A_tail_that_does_not_match_the_master_stops_before_anything_is_added()
    {
        var master = new List<string[]> { Row(1), Row(2), Row(3) };
        var grid = Grid(master[0], Row(2, "Someone Else"));
        var plan = SheetPlan.Plan(master, grid);
        Assert.Empty(plan.Appends);
        Assert.Contains("drifted", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gap_in_the_display_numbers_stops_before_anything_is_added()
    {
        var master = new List<string[]> { Row(1), Row(2), Row(3) };
        var grid = Grid(master[0], master[1]);
        grid[2] = ["3", .. master[1][1..]];
        var plan = SheetPlan.Plan(master, grid);
        Assert.Empty(plan.Appends);
        Assert.Contains("not 1..N", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_ahead_of_the_master_adds_nothing()
    {
        var plan = SheetPlan.Plan([Row(1)], Grid(Row(1), Row(2)));
        Assert.Empty(plan.Appends);
        Assert.Contains("ahead of the master", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_banned_row_stops_the_append_there_and_every_later_row_waits()
    {
        var master = new List<string[]> { Row(1), Row(2), Row(3, "NA", "NA", "Email", "Inbox swept, nothing new"), Row(4) };
        var plan = SheetPlan.Plan(master, Grid(master[0]));
        Assert.Equal(["2"], plan.Counts);
        Assert.Equal(3, plan.StoppedAt);
        Assert.Contains("banned by the tracker gate", plan.Reason, StringComparison.Ordinal);
        Assert.False(plan.InStep);
    }

    [Theory]
    [InlineData("SHADOW sheet 2993 master 2996 add 3 (Counts 2994 to 2996)", SheetMode.Shadow, "planned", null)]
    [InlineData("ON sheet 2993 master 2996 add 3 (Counts 2994 to 2996) | VERIFIED 2996 rows, 1..2996, the 3 added read back cell for cell", SheetMode.On, "success", null)]
    [InlineData("SHADOW sheet 2852 master 2994 add 0 STOPPED at 2994: a banned row", SheetMode.Shadow, "blocked", "gate")]
    [InlineData("ON sheet 2852 master 2994 add 0 STOPPED at 2994: a banned row", SheetMode.On, "blocked", "gate")]
    [InlineData("ON sheet 2993 master 2996 add 3 | MISMATCH: the Sheet reads 2995 rows after the append, 2996 expected", SheetMode.On, "blocked", "mismatch")]
    public void A_plan_the_gate_stopped_is_blocked_on_the_gate_and_not_planned(string line, SheetMode mode, string outcome, string? detail) =>
        Assert.Equal((outcome, detail), SheetMirrorTimer.Outcome(line, mode));

    [Fact]
    public void A_sentence_in_communication_stops_the_append_there()
    {
        var master = new List<string[]> { Row(1), Row(2, comm: "SENT by Steve 7:50 AM CDT (msg 1a2b)"), Row(3) };
        var plan = SheetPlan.Plan(master, Grid(master[0]));
        Assert.Empty(plan.Appends);
        Assert.Equal(2, plan.StoppedAt);
        Assert.Contains("not a channel name", plan.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LinkedIn", true)]
    [InlineData("Email", true)]
    [InlineData("Greenhouse", true)]
    [InlineData("LinkedIn connection invite w/ note", true)]
    [InlineData("Company Website", true)]
    [InlineData("Workday (servicetitan.wd1)", true)]
    [InlineData("Greenhouse / Ashby / Workday", true)]
    [InlineData("Manager, Software Development", false)]
    [InlineData("Remote Manager, Software Development", false)]
    [InlineData("Email received: ATS rejection", false)]
    [InlineData("Email received + reply DRAFTED (not sent)", false)]
    [InlineData("ZipRecruiter 'Lead .NET Developer' Remote sweep, page 2 of 1", false)]
    [InlineData("", false)]
    public void Communication_is_a_channel_name_never_a_sentence(string cell, bool channel) =>
        Assert.Equal(channel, SheetPlan.IsChannel(cell));

    [Theory]
    [InlineData("Acme", "Application submitted", null)]
    [InlineData("NA", "Inbox swept, nothing new", "no counterparty")]
    [InlineData("Acme", "Form filled but not submitted", "nothing reached")]
    [InlineData("Acme", "Form filled but not submitted; she replied the next day", null)]
    [InlineData("Acme", "Recruiter wrote about a role", null)]
    public void The_gate_bans_what_check_tracker_bans_and_nothing_more(string company, string notes, string? why)
    {
        string? banned = TrackerGate.Banned(Row(1, company == "NA" ? "NA" : "Ann Example", company, "Email", notes));
        if (why is null)
        {
            Assert.Null(banned);
        }
        else
        {
            Assert.Contains(why, banned, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Shadow_writes_the_plan_and_the_log_and_adds_nothing_to_the_sheet()
    {
        using var scratch = new Scratch();
        scratch.Master(Csv(Row(1), Row(2), Row(3)));
        var grid = new MemoryGrid(Grid(Row(1)));
        string line = await new SheetMirror(scratch.Store, grid, new FixedTime(Noon)).RunAsync(SheetMode.Shadow, CancellationToken.None);
        Assert.Equal(0, grid.Appends);
        Assert.StartsWith("SHADOW sheet 1 master 3 add 2 (Counts 2 to 3)", line, StringComparison.Ordinal);
        string plan = File.ReadAllText(Path.Combine(scratch.Root, "sheet-mirror", "last-plan.tsv"));
        Assert.Contains("2\t2\tAnn Example\tAcme\tEmail", plan, StringComparison.Ordinal);
        Assert.Contains("12:00:00 SHADOW", File.ReadAllText(Path.Combine(scratch.Root, "sheet-mirror", "mirror.log")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_appends_the_plan_and_reads_it_back_cell_for_cell()
    {
        using var scratch = new Scratch();
        scratch.Master(Csv(Row(1), Row(2), Row(3)));
        var grid = new MemoryGrid(Grid(Row(1)));
        string line = await new SheetMirror(scratch.Store, grid, new FixedTime(Noon)).RunAsync(SheetMode.On, CancellationToken.None);
        Assert.Equal(1, grid.Appends);
        Assert.EndsWith("VERIFIED 3 rows, 1..3, the 2 added read back cell for cell", line, StringComparison.Ordinal);
        Assert.Equal(4, grid.Rows.Count);
    }

    [Fact]
    public async Task On_says_so_when_the_read_back_differs()
    {
        using var scratch = new Scratch();
        scratch.Master(Csv(Row(1), Row(2)));
        var grid = new MemoryGrid(Grid(Row(1))) { Mangle = true };
        string line = await new SheetMirror(scratch.Store, grid, new FixedTime(Noon)).RunAsync(SheetMode.On, CancellationToken.None);
        Assert.Contains("MISMATCH", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shadow", SheetMode.Shadow)]
    [InlineData("ON", SheetMode.On)]
    [InlineData("off", SheetMode.Off)]
    [InlineData(null, SheetMode.Off)]
    [InlineData("yes", SheetMode.Off)]
    public void The_setting_names_the_mode_and_anything_else_is_off(string? value, SheetMode mode) =>
        Assert.Equal(mode, SheetMirrorTimer.Mode(value));

    [Fact]
    public void The_grid_port_can_read_and_append_and_nothing_else()
    {
        var methods = typeof(ISheetGrid).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Order().ToList();
        Assert.Equal(["AppendAsync", "ReadAsync"], methods);
    }

    [Fact]
    public void No_code_under_sheet_can_edit_clear_or_delete_a_cell_or_touch_gmail()
    {
        string code = SliceCode(TemplateRoot(), "Sheet");
        foreach (string forbidden in new[] { ":batchUpdate", ":clear", "PutAsync", "DeleteAsync", "HttpMethod.Put", "HttpMethod.Delete", "gmail.googleapis.com", "valueInputOption=USER_ENTERED" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
        Assert.Contains("insertDataOption=INSERT_ROWS", code, StringComparison.Ordinal);
    }

    private static string Csv(params string[][] rows) => string.Join("\r\n", rows.Select(TrackerCsvText.Line)) + "\r\n";

    /// <summary>Every .cs file of one slice, read across the Function's three rings (ADR-117).</summary>
    private static string SliceCode(string root, string slice) => string.Join("\n", new[] { "JobHunt.Domain", "JobHunt.Application", "JobHunt.Infrastructure" }
        .Select(ring => Path.Combine(root, "functions", ring, "Features", slice))
        .Where(Directory.Exists)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        .Select(File.ReadAllText));

    private static string TemplateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt.bicep not found above " + AppContext.BaseDirectory);
    }

    /// <summary>A Sheet1 in memory: reads what it holds, appends at the end, and can garble its read-back.</summary>
    private sealed class MemoryGrid(List<IReadOnlyList<string>> rows) : ISheetGrid
    {
        public List<IReadOnlyList<string>> Rows { get; } = rows;

        public int Appends { get; private set; }

        public bool Mangle { get; init; }

        public Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IReadOnlyList<string>>>([.. Rows]);

        public Task AppendAsync(IReadOnlyList<IReadOnlyList<string>> added, CancellationToken ct)
        {
            Appends++;
            Rows.AddRange(added.Select(row => Mangle ? (IReadOnlyList<string>)[.. row.Take(6), "garbled"] : row));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
