using TheYard.Api;
using TheYard.Application;

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
        // The size row names the next Basic size and what it adds, from the size Azure returned.
        Assert.Equal(("B3", 24.82), PlanView.NextSize("B2"));
        Assert.Equal(("B2", 12.41), PlanView.NextSize("b1"));
        Assert.Null(PlanView.NextSize("B3"));
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
    }
}
