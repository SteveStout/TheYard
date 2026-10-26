
namespace TheYard.Tests;

/// <summary>
/// The App Service plan card from fixed minutes in to a fixed report out (ADR: The App Service
/// plan, read every minute): no Azure, no store, so what the card says about a given hour is
/// held here rather than discovered on the live site.
/// </summary>
public class PlanViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 15, 0, 0, TimeSpan.Zero);
    private const string Plan = "/subscriptions/sub/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Web/serverfarms/PLAN-SS";
    private const string Sql = "/subscriptions/sub/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Web/sites/APP-THEYARD-SS";

    private static PlanMinute Minute(int minutesAgo, double cpu, double memory, string version = "1.0.3.170") => new(
        Now.AddMinutes(-minutesAgo), cpu, memory, 0.1, 0, 1_000_000, 500_000, 154, version,
        [new PlanAppMinute("APP-THEYARD-SS", 6, 300, 120, 0, 4, 2_048), new PlanAppMinute("app-theyard-render-ss", 2, 150, 60, 0, 2, 1_024)]);

    private static PlanHour Hour(params PlanEvent[] events) => new(
        Now.AddHours(-1), Plan, "tenant-1", "B1", 1, 1, 1.75,
        [new PlanAppState("APP-THEYARD-SS", Sql, "Running", true, Now.AddDays(-1), "app,linux,container", "DOCKER|cr.azurecr.io/theyard:v1.0.3.170", ["theyard.example", "app-theyard-ss.azurewebsites.net"]), new PlanAppState("app-theyard-render-ss", Sql.Replace("APP-THEYARD-SS", "app-theyard-render-ss"), "Running", true, null)],
        events, "West US 3", true);

    private static CloudReport Build(IReadOnlyList<PlanMinute> minutes, params PlanHour[] hours) => PlanView.Build(
        "24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in Azure Cosmos DB for 31 days"),
        minutes, PlanFolding.Buckets(minutes, MachineGrain.FiveMinutes), PlanFolding.Spread(minutes), hours, 13.14);

    // #region plan-view-tests
    [Fact]
    public void A_quiet_hour_reads_as_fine_with_the_sizes_and_links_azure_returned()
    {
        var minutes = Enumerable.Range(0, 60).Reverse().Select(i => Minute(i, 23, 40)).ToList();
        var report = Build(minutes, Hour());

        Assert.True(report.Available);
        Assert.Equal("B1 LINUX 1 VCPU / 1.75 GB WEST US 3", report.Spec);
        // The thresholds are chips written from PlanThresholds (CardFrame), and the size row names no price and no
        // size Azure did not return (ADR: The onion cut into vertical slices, the facts the tab reads).
        Assert.DoesNotContain("$", report.Rows.Single(row => row.Key == "size").Detail[0].Text, StringComparison.Ordinal);
        // Azure refuses a whole metrics read that names one metric the resource does not report, or
        // one it does not keep at a minute's grain, so the minute's names are held to what the Linux
        // plan and its apps report at PT1M (az monitor metrics list-definitions and a read at PT1M,
        // 18 October). AppConnections, a Windows plan's metric, cost the first roll; FileSystemUsage,
        // kept at six hours, cost the second, and is read apart at its own grain.
        string[] planReports = ["BytesReceived", "BytesSent", "CpuPercentage", "DiskQueueLength", "HttpQueueLength", "MemoryPercentage", "SocketInboundAll", "SocketOutboundAll"];
        string[] appReports = ["CpuTime", "Http5xx", "MemoryWorkingSet", "Requests", "BytesReceived", "BytesSent", "HttpResponseTime"];
        Assert.Equal("FileSystemUsage", PlanReader.FileSystem.Name);
        Assert.All(PlanReader.PlanMetrics, metric => Assert.Contains(metric.Name, planReports));
        Assert.All(PlanReader.AppMetrics, metric => Assert.Contains(metric.Name, appReports));
        Assert.Equal("https://portal.azure.com/#@tenant-1/resource" + Plan, report.Resource!.Href);
        Assert.Equal("23 %", report.Dials[0].Big);
        Assert.Equal("0.23 of 1 vCPU", report.Dials[0].Absolute);
        Assert.Equal("0.70 of 1.75 GB", report.Dials[1].Absolute);
        Assert.All(report.Rows, row => Assert.Contains(row.Tone, new[] { "good", "info" }));
        Assert.Equal("2 apps, running", report.Rows.Single(r => r.Key == "power").Answer);
        // The apps are a table, each name a link to its blade, and each described from Azure's own
        // read of the site: what it is, the image it runs and the names it answers to.
        var apps = report.Rows.Single(r => r.Key == "power").Table;
        Assert.Contains(apps.Rows, row => row[0].Href == "https://portal.azure.com/#@tenant-1/resource" + Sql);
        Assert.Equal(["Web app, Linux container", "theyard:v1.0.3.170", "theyard.example, app-theyard-ss.azurewebsites.net"], apps.Rows[0].Skip(1).Take(3).Select(cell => cell.Text));
        // An hour kept before the reader read these says so, and never guesses from the name.
        Assert.Equal(["not read", "not read", "not read"], apps.Rows[1].Skip(1).Take(3).Select(cell => cell.Text));
        // Each app with what it holds and burns now, and a last row for the plan outside every app.
        Assert.Equal(new[] { "App", "Type", "Runs", "Answers to", "State", "Always on", "Memory now", "CPU now" }, apps.Columns);
        Assert.Equal("the plan, outside the apps", apps.Rows[^1][0].Text);
        // The row says it is a difference, not a reading (ADR: The App Service plan, read every minute, 19 October).
        Assert.Equal("Linux and the platform (derived: plan memory used minus the apps)", apps.Rows[^1][1].Text);
        Assert.Equal(12, report.Readout.Lines.Count);
        Assert.Equal("$13.14 / mo", report.Readout.Lines.Single(l => l.Label == "Plan cost").Value);
    }

    [Fact]
    public void Nothing_kept_is_a_sentence_and_never_a_zero()
    {
        var report = PlanView.Build("24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(false, "the plan container is not there yet; apply infra/cosmos/plan.json"),
            [], [], new PlanSpread(0, null, null, null, null), [], null);

        Assert.False(report.Available);
        Assert.Equal("the plan container is not there yet; apply infra/cosmos/plan.json", report.Note);
        Assert.All(report.Dials, dial => Assert.Null(dial.Percent));
        Assert.DoesNotContain(report.Readout.Lines, line => line.Value is "0" or "0 %" or "$0.00 / mo");
        Assert.Null(report.Resource);
    }

    [Fact]
    public void A_new_build_in_a_bucket_is_a_gold_deploy_and_an_unplanned_restart_is_red()
    {
        var minutes = Enumerable.Range(0, 60).Reverse().Select(i => Minute(i, 30, 50, i > 30 ? "1.0.3.169" : "1.0.3.170")).ToList();
        var restart = new PlanEvent(Now.AddMinutes(-20), "restart", "Restart Web App", "APP-THEYARD-SS", Planned: false);
        var report = Build(minutes, Hour(restart));

        Assert.Single(report.Chart.Marks, mark => mark.Kind == "deploy" && mark.Title == "Deployed 1.0.3.170");
        Assert.Single(report.Chart.Marks, mark => mark.Kind == "event");
        Assert.Equal("alarm", report.Rows.Single(r => r.Key == "azure").Tone);
        Assert.Equal("alarm", report.Readout.Lines.Single(l => l.Label == "Restarts, 30 d").Tone);
    }
    // #endregion plan-view-tests

    [Fact]
    public void Each_series_carries_its_own_lines_and_each_point_its_buckets_peak()
    {
        // CPU and memory turn at different levels (amber 60 and red 80, amber 70 and red 85), so each series carries its
        // own and the chart draws one panel each, never one 80 % line that belongs to only one of them (ADR-102, the
        // addendum on how a card draws time). A spike averaged into its bucket is kept as the bucket's peak.
        // Fifteen minutes, three whole buckets of five, each with one minute at 90 and four at 10.
        var minutes = Enumerable.Range(6, 15).Reverse().Select(i => Minute(i, (60 - i) % 5 == 0 ? 90 : 10, 50)).ToList();
        var report = Build(minutes, Hour());
        var (cpu, memory) = (report.Chart.Series[0], report.Chart.Series[1]);

        Assert.Equal(("CPU", 60d, 80d), (cpu.Name, cpu.Amber!.Value, cpu.Red!.Value));
        Assert.Equal(("Memory", 70d, 85d), (memory.Name, memory.Amber!.Value, memory.Red!.Value));
        Assert.All(cpu.Points, point => Assert.Equal(90d, point.Peak!.Value, 6));
        Assert.All(cpu.Points, point => Assert.True(point.Value < point.Peak));
        Assert.All(memory.Points, point => Assert.Equal(50d, point.Peak!.Value, 6));
    }

    [Fact]
    public void A_configuration_write_is_a_counted_change_and_only_a_restart_is_drawn_at_full_height()
    {
        // Each roll writes both web apps' configuration and the finished entry names no caller, so a week of rolls
        // was drawn as two hundred full-height red lines "by Azure" (read live on 26 October: 221 in seven days).
        var minutes = Enumerable.Range(0, 30).Reverse().Select(i => Minute(i, 30, 50)).ToList();
        var write = new PlanEvent(Now.AddMinutes(-25), "config", "UpdateWebSite", "APP-THEYARD-SS", Planned: false);
        var restart = new PlanEvent(Now.AddMinutes(-20), "restart", "Restart Web App", "APP-THEYARD-SS", Planned: false);
        var report = Build(minutes, Hour(write, restart));

        Assert.Single(report.Chart.Marks, mark => mark.Kind == "change" && mark.Title.StartsWith("UpdateWebSite", StringComparison.Ordinal));
        Assert.Single(report.Chart.Marks, mark => mark.Kind == "event" && mark.Title.StartsWith("Restart Web App", StringComparison.Ordinal));
        Assert.Equal("change", CloudMarks.KindOf(write));
        Assert.Equal("event", CloudMarks.KindOf(new PlanEvent(Now, "maintenance", "Planned maintenance", "PLAN", Planned: true)));
    }

    [Fact]
    public void A_deploy_is_a_shaded_span_and_a_spike_inside_it_is_named_a_blip()
    {
        var t = new DateTimeOffset(2026, 10, 18, 20, 40, 0, TimeSpan.Zero);
        // The roll as Azure logged it: the settings write, then the image, on two apps a minute apart.
        PlanEvent[] events =
        [
            new(t, "config", "Write web app config", "APP-THEYARD-SS", Planned: true),
            new(t.AddMinutes(1), "config", "Write web app config", "APP-THEYARD-COSMOS-SS", Planned: true),
            new(t.AddHours(2), "restart", "Restart", "APP-THEYARD-SS", Planned: true),
        ];
        PlanBucket[] buckets =
        [
            new(t.AddMinutes(-5), 5, 10, 12, 60, 61, 0, "1.0.3.175"),
            new(t, 5, 40, 74, 66, 68, 0, "1.0.3.176"),
            new(t.AddMinutes(30), 5, 9, 10, 60, 61, 0, "1.0.3.176"),
        ];
        var rolls = PlanDeploys.Rolls(events, buckets);
        var roll = Assert.Single(rolls);
        Assert.Equal("Deploy 1.0.3.176: a new container beside the old, then the swap", roll.Title);
        Assert.Equal(t.ToUnixTimeMilliseconds(), roll.StartMs);
        Assert.Equal(t.AddMinutes(4).ToUnixTimeMilliseconds(), roll.EndMs);
        var blip = Assert.Single(PlanDeploys.Blips(rolls, buckets));
        Assert.StartsWith("CPU reached 74 % in the roll at Oct 18 20:40 UTC, a blip", blip, StringComparison.Ordinal);
        Assert.Equal("kept since Oct 18 20:35 UTC", PlanDeploys.KeptSince(buckets, t.AddDays(-1), TimeSpan.FromMinutes(30)));

        // On a window of minutes the old container records on for ten minutes after the write, so
        // the first bucket after the write still says the old build; the band is named for the
        // first build inside the half hour that differs (the review of 1.0.4.0, finding 11: every
        // band on the 24-hour window read "a configuration change").
        PlanBucket[] minutes =
        [
            new(t.AddMinutes(-1), 1, 10, 12, 60, 61, 0, "1.0.3.175"),
            new(t, 1, 10, 12, 60, 61, 0, "1.0.3.175"),
            new(t.AddMinutes(9), 1, 40, 74, 66, 68, 0, "1.0.3.175"),
            new(t.AddMinutes(11), 1, 40, 74, 66, 68, 0, "1.0.3.176"),
            new(t.AddMinutes(40), 1, 9, 10, 60, 61, 0, "1.0.3.176"),
        ];
        Assert.Equal("Deploy 1.0.3.176: a new container beside the old, then the swap", Assert.Single(PlanDeploys.Rolls(events, minutes)).Title);
        Assert.Equal("A configuration change on the plan's apps", Assert.Single(PlanDeploys.Rolls(events, [minutes[0], minutes[1], minutes[2]])).Title);
    }

    /// <summary>The edges, a minute either side: writes ten minutes apart are one roll and eleven are two; a build thirty minutes after the first write names the roll and one at thirty-one does not; a spike at the amber line is a blip and one under it is not.</summary>
    [Theory]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    public void Writes_ten_minutes_apart_are_one_roll_and_eleven_are_two(int apart, int rolls)
    {
        var t = new DateTimeOffset(2026, 10, 18, 20, 40, 0, TimeSpan.Zero);
        PlanEvent[] events = [new(t, "config", "Write web app config", "a", Planned: true), new(t.AddMinutes(apart), "config", "Write web app config", "a", Planned: true)];
        Assert.Equal(rolls, PlanDeploys.Rolls(events, []).Count);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void The_new_build_names_the_roll_within_thirty_minutes_of_its_first_write(int after, bool named)
    {
        var t = new DateTimeOffset(2026, 10, 18, 20, 40, 0, TimeSpan.Zero);
        PlanEvent[] events = [new(t, "config", "Write web app config", "a", Planned: true)];
        PlanBucket[] minutes = [new(t.AddMinutes(-1), 1, 10, 12, 60, 61, 0, "1.0.3.175"), new(t, 1, 10, 12, 60, 61, 0, "1.0.3.175"), new(t.AddMinutes(after), 1, 10, 12, 60, 61, 0, "1.0.3.176")];
        string title = Assert.Single(PlanDeploys.Rolls(events, minutes)).Title;
        Assert.Equal(named, title.StartsWith("Deploy 1.0.3.176", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    public void A_spike_at_the_amber_line_is_a_blip_and_one_under_it_is_not(double fromAmber, int blips)
    {
        var t = new DateTimeOffset(2026, 10, 18, 20, 40, 0, TimeSpan.Zero);
        var band = new CloudBand(t.ToUnixTimeMilliseconds(), t.AddMinutes(4).ToUnixTimeMilliseconds(), "a roll");
        double cpu = PlanThresholds.Cpu.Amber + fromAmber;
        PlanBucket[] buckets = [new(t, 5, cpu, cpu, 10, 10, 0, "1.0.3.176")];
        Assert.Equal(blips, PlanDeploys.Blips([band], buckets).Count);
    }
}
