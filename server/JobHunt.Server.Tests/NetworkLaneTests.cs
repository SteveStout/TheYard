namespace JobHunt.Server.Tests;

/// <summary>
/// The network lane, the network log's LinkedIn half: it runs after the messages lane, reads and never acts, and the
/// program spools only rows that hold with their text, into the one folder the Function reads, and moves the mark.
/// </summary>
public sealed class NetworkLaneTests
{
    private const string Folder = "nightly/2026-11-03/0600-network-sonnet-5-5";
    private const string Row = "Pat Lee\tAcme\thttps://www.linkedin.com/in/patlee/\t2-abc123==\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00";

    private static string Tsv(params string[] rows) => NetworkThreads.Header + "\n" + string.Join("\n", rows) + "\n";

    [Fact]
    public void The_network_lane_runs_after_messages_and_sends_nothing()
    {
        Assert.Equal([Lane.Feed, Lane.Invites, Lane.Messages, Lane.Network, Lane.Apply], RunSlot.Order);
        Assert.Equal("network", RunSlot.Word(Lane.Network));
        Assert.Equal("LINKEDIN-NETWORK-LANE.md", RunSlot.Doc(Lane.Network));
        Assert.False(RunSlot.OnLinkedIn(Lane.Network));
        Assert.Equal(Lane.Network, RunSlot.Parse("network"));
    }

    [Fact]
    public void The_header_is_the_Function_s_spool_header()
    {
        Assert.Equal("Person\tCompany\tProfileUrl\tThreadId\tLastInbound\tLastOutbound\tFirstTwoWay", NetworkThreads.Header);
    }

    [Theory]
    [InlineData("networklog/linkedin-inbox/20261103-120000Z-server6am.tsv", true)]
    [InlineData("networklog/linkedin-inbox/text/2-abc123==.txt", true)]
    [InlineData("networklog/linkedin-high-water.txt", true)]
    [InlineData("networklog/linkedin-floor.txt", true)]
    [InlineData("networklog/linkedin-inbox/done/x.tsv", false)]
    [InlineData("networklog/linkedin-inbox/x.txt", false)]
    [InlineData("networklog/linkedin-inbox/text/a/b.txt", false)]
    [InlineData("networklog/pass.log", false)]
    [InlineData("networklog/topics-needed.tsv", false)]
    public void The_program_writes_the_network_log_s_LinkedIn_spool_and_nothing_else_of_it(string path, bool allowed) =>
        Assert.Equal(allowed, WritePolicy.Allowed(path));

    [Fact]
    public void A_row_holds_only_with_a_profile_a_safe_thread_id_and_both_sides_times()
    {
        var (rows, refused) = NetworkThreads.Parse(Tsv(
            Row,
            "Sam Ortiz\tGlobex\thttps://www.linkedin.com/company/globex\t2-def\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00",
            "Kim Hot\tDelta\thttps://www.linkedin.com/in/kimhot\t../../state\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00",
            "Bo Cold\tKappa\thttps://www.linkedin.com/in/bocold\t2-ghi\t2026-11-02T09:15:00-06:00\t\t2026-10-01T08:00:00-05:00",
            "Cy Late\tIota\thttps://www.linkedin.com/in/cylate\t2-jkl\t2026-11-02T09:15:00-06:00\t2026-11-02T10:00:00-06:00\t2026-11-03T08:00:00-06:00",
            "Di Local\tMu\thttps://www.linkedin.com/in/dilocal\t2-mno\t2026-11-02 09:15\t2026-11-02T10:00:00-06:00\t2026-10-01T08:00:00-05:00",
            Row));
        NetworkThread pat = Assert.Single(rows);
        Assert.Equal("2-abc123==", pat.ThreadId);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 9, 15, 0, TimeSpan.FromHours(-6)), pat.LastInbound);
        Assert.Equal(6, refused.Count);
        Assert.Contains(refused, why => why.StartsWith("line 3: needs a profile", StringComparison.Ordinal));
        Assert.Contains(refused, why => why.StartsWith("line 4: needs a thread id", StringComparison.Ordinal));
        Assert.Contains(refused, why => why.StartsWith("line 5: needs both sides", StringComparison.Ordinal));
        Assert.Contains(refused, why => why.StartsWith("line 6: the first two-way time", StringComparison.Ordinal));
        Assert.Contains(refused, why => why.StartsWith("line 7: needs both sides", StringComparison.Ordinal));
        Assert.Contains(refused, why => why.StartsWith("line 8: the same thread twice", StringComparison.Ordinal));
        Assert.Equal(["line 1: not the network lane's header"], NetworkThreads.Parse("Person\tCompany\n" + Row).Refused);
    }

    [Fact]
    public void The_mark_moves_only_forward()
    {
        var (rows, _) = NetworkThreads.Parse(Tsv(Row));
        var older = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-6)), NetworkThreads.HighWater(rows, older));
        Assert.Equal(newer, NetworkThreads.HighWater(rows, newer));
        Assert.Equal(older, NetworkThreads.HighWater([], older));
        Assert.Null(NetworkThreads.HighWater([], null));
        Assert.Equal(older, NetworkThreads.Floor(rows, older));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-6)), NetworkThreads.Floor(rows, newer));
        Assert.Null(NetworkThreads.Floor([], null));
    }

    [Fact]
    public async Task The_backlog_reads_done_once_the_model_says_so_and_stays_done()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{Folder}/{NetworkLane.Threads}"] = Tsv(Row);
            morning.Share.Files[$"{Folder}/threads/2-abc123==.txt"] = "Pat Lee: Hello.\n";
            morning.Share.Files[$"{Folder}/{NetworkLane.BacklogDone}"] = "reached 46 days\n";
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["network"].Enabled = true;
        morning.Share.Files[WritePolicy.Floor] = "2026-10-10T08:00:00-05:00\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Network), default);
        Assert.Equal("mark: none\nfloor: 2026-10-10T08:00:00-05:00\n", morning.Share.Files[$"{Folder}/high-water.txt"]);
        Assert.Equal("done\n", morning.Share.Files[WritePolicy.Floor]);
    }

    [Fact]
    public void The_network_lane_reads_and_scrolls_and_never_fills()
    {
        var live = ClaudeArguments.For(new LaneConfig(), Lane.Network, "/mnt/j", "/mnt/j/run", dry: false);
        var dry = ClaudeArguments.For(new LaneConfig(), Lane.Network, "/mnt/j", "/mnt/j/run", dry: true);
        Assert.DoesNotContain(live, tool => tool.EndsWith("__form_input", StringComparison.Ordinal) || tool.EndsWith("__file_upload", StringComparison.Ordinal));
        Assert.Contains(live, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.Contains("LINKEDIN-NETWORK-LANE.md", live[1], StringComparison.Ordinal);
        Assert.Contains("/mnt/j/run/network-threads.tsv", live[1], StringComparison.Ordinal);
        Assert.Contains("/mnt/j/run/high-water.txt", live[1], StringComparison.Ordinal);
        Assert.DoesNotContain(live, tool => tool.StartsWith("Read(//mnt/j/Job_Hunt_Tracker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_live_run_spools_the_rows_with_their_text_and_moves_the_mark()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{Folder}/{NetworkLane.Threads}"] = Tsv(Row, Row.Replace("Pat Lee", "No Text", StringComparison.Ordinal).Replace("patlee", "notext", StringComparison.Ordinal).Replace("2-abc123==", "2-xyz", StringComparison.Ordinal));
            morning.Share.Files[$"{Folder}/threads/2-abc123==.txt"] = "Pat Lee: Good to hear from you.\nSteve: Likewise.\n";
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["network"].Enabled = true;
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Network), default);

        Assert.Equal("mark: none\nfloor: none\n", morning.Share.Files[$"{Folder}/high-water.txt"]);
        string spool = Assert.Single(morning.Share.Files.Keys, path => path.StartsWith(WritePolicy.LinkedInInbox, StringComparison.Ordinal) && path.EndsWith(".tsv", StringComparison.Ordinal));
        Assert.Equal("networklog/linkedin-inbox/20261103-120000Z-server6am.tsv", spool);
        Assert.Equal(Tsv(Row), morning.Share.Files[spool]);
        Assert.Equal("Pat Lee: Good to hear from you.\nSteve: Likewise.\n", morning.Share.Files["networklog/linkedin-inbox/text/2-abc123==.txt"]);
        Assert.Equal("2026-11-02T10:00:00-06:00\n", morning.Share.Files[WritePolicy.HighWater]);
        Assert.Equal("2026-11-02T10:00:00-06:00\n", morning.Share.Files[WritePolicy.Floor]);
        Assert.Contains("network: 1 two-way threads spooled for the network log, 1 left out", output, StringComparison.Ordinal);
        Assert.Contains("no text at", morning.Share.Files[$"{Folder}/DONE.md"], StringComparison.Ordinal);
        Assert.Empty(morning.TrackerRows());
    }

    [Fact]
    public async Task A_dry_run_spools_nothing_and_the_next_run_starts_from_the_mark()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{Folder}-dry/{NetworkLane.Threads}"] = Tsv(Row);
            morning.Share.Files[$"{Folder}-dry/threads/2-abc123==.txt"] = "Pat Lee: Hello.\n";
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["network"].Enabled = true;
        morning.Share.Files[WritePolicy.HighWater] = "2026-10-20T08:00:00-05:00\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(true, Lane.Network), default);

        Assert.Equal("mark: 2026-10-20T08:00:00-05:00\nfloor: none\n", morning.Share.Files[$"{Folder}-dry/high-water.txt"]);
        Assert.Equal("2026-10-20T08:00:00-05:00\n", morning.Share.Files[WritePolicy.HighWater]);
        Assert.DoesNotContain(morning.Share.Files.Keys, path => path.StartsWith(WritePolicy.LinkedInInbox, StringComparison.Ordinal));
        Assert.Contains("Dry run: nothing spooled", morning.Share.Files[$"{Folder}-dry/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_LinkedIn_driver_holds_the_network_lane()
    {
        var morning = new Morning(_ => new ClaudeRun(0, false, ResultLine.Empty));
        morning.Config.Lanes["network"].Enabled = true;
        morning.Share.Files["nightly/2026-11-03/0600-linkedin-closed.md"] = "the messages lane met checkpoint\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Network), default);
        Assert.Contains("network: held, the messages lane met checkpoint", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }
}
