using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

// #region process-readers-tests
/// <summary>
/// The job hunt Function's runs as the site reads them (ADR-109, the addendum on the six processes): the Kusto
/// reader over a recorded workspace answer, its one query shape, the trace link, the summaries read from the
/// Function's own code, the switches kept from the health route, and the runs route's 200 and 404.
/// </summary>
public sealed class ProcessReadersTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Workspace = RecordedCloud.Workspace;

    [Fact]
    public async Task The_reader_reads_a_page_its_totals_the_days_and_the_newest_from_the_workspace()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var reader = new InsightsProcessRuns(new AzureMetrics("client-1", configured: true), Workspace, "func-jobhunt-ss");

        var read = await reader.RunsAsync(FunctionProcesses.Find("AtsSweep")!, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);

        Assert.True(read.Available);
        Assert.Equal(2, read.Rows.Count);
        Assert.Equal(("success", 42L, "op-1"), (read.Rows[0].Outcome, read.Rows[0].Counts["read"], read.Rows[0].OperationId));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 16, 20, 0, 500, TimeSpan.Zero), read.Rows[0].At);
        Assert.Equal(("blocked", "google_token"), (read.Rows[1].Outcome, read.Rows[1].Detail));
        Assert.Equal((188L, 2L, 2L, 99_000d), (read.Totals.Runs, read.Totals.Late, read.Totals.Blocked, read.Totals.P95Ms!.Value));

        var fold = await reader.FoldAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 24), fold.Days.Single().Day);
        Assert.Equal(22, fold.Days.Single().Tally.Runs);
        Assert.Equal((true, false), (fold.Last["AtsSweep"].HasLine, fold.Last["Merger"].HasLine));
        Assert.Contains(cloud.Asked, address => address.Contains("log-theyard-ss/api/query", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_workspace_and_a_site_off_Azure_read_nothing_and_say_why()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud { QueryForbidden = true });
        var refused = new InsightsProcessRuns(new AzureMetrics("client-1", configured: true), Workspace, "func-jobhunt-ss");

        var fold = await refused.FoldAsync(TimeSpan.FromDays(30), CancellationToken.None);
        Assert.False(fold.Available);
        Assert.Contains("403", fold.Note, StringComparison.Ordinal);

        var off = new InsightsProcessRuns(new AzureMetrics("client-1", configured: false), Workspace, "func-jobhunt-ss");
        var page = await off.RunsAsync(FunctionProcesses.Find("Merger")!, TimeSpan.FromDays(1), 0, 20, CancellationToken.None);
        Assert.False(page.Available);
        Assert.Contains("off Azure", page.Note, StringComparison.Ordinal);
    }

    private sealed class CountingRuns : TheYard.Application.Features.Admin.Cloud.IProcessRunsReader
    {
        public int Reads { get; private set; }

        public Task<TheYard.Application.Features.Admin.Cloud.ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
        {
            Reads++;
            return Task.FromResult(new TheYard.Application.Features.Admin.Cloud.ProcessRunsRead(true, null, [], TheYard.Application.Features.Admin.Cloud.ProcessTally.None));
        }

        public Task<TheYard.Application.Features.Admin.Cloud.ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(TheYard.Application.Features.Admin.Cloud.ProcessFold.Unread("test"));

        public string? TraceHref(string operationId) => null;

        public string? ProcessHref(string process) => null;
    }

    [Fact]
    public async Task The_route_s_reader_keeps_a_page_two_minutes_and_reads_at_most_thirty_fresh_pages_a_minute()
    {
        var inner = new CountingRuns();
        var guarded = new GuardedProcessRuns(inner, TimeProvider.System);
        var merger = FunctionProcesses.Find("Merger")!;

        await guarded.RunsAsync(merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);
        await guarded.RunsAsync(merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);
        Assert.Equal(1, inner.Reads);

        var answers = new List<TheYard.Application.Features.Admin.Cloud.ProcessRunsRead>();
        for (int page = 1; page <= GuardedProcessRuns.FreshReadsAMinute + 5; page++)
        {
            answers.Add(await guarded.RunsAsync(merger, TimeSpan.FromDays(7), page * 20, 20, CancellationToken.None));
        }
        Assert.True(inner.Reads <= GuardedProcessRuns.FreshReadsAMinute + 1);
        Assert.Contains(answers, read => !read.Available && read.Note!.StartsWith("busy", StringComparison.Ordinal));
    }

    [Fact]
    public void The_query_is_one_shape_filtered_to_the_Function_s_role_and_the_six_names()
    {
        var reader = new InsightsProcessRuns(new AzureMetrics("client-1", configured: false), Workspace, "func-jobhunt-ss");

        string kusto = reader.Kusto(TimeSpan.FromDays(7), InsightsProcessRuns.Totals.Replace("__PROCESS__", "Merger", StringComparison.Ordinal));

        Assert.Contains("let role = 'func-jobhunt-ss';", kusto, StringComparison.Ordinal);
        Assert.Contains("ago(168h)", kusto, StringComparison.Ordinal);
        Assert.Contains("Name in ('AtsSweep', 'Merger', 'Nightly', 'SheetMirror', 'DailyCard', 'CampaignSend', 'MergerHealth')", kusto, StringComparison.Ordinal);
        Assert.Contains("Process in ('AtsSweep', 'SheetMirror', 'MergerHealth')", kusto, StringComparison.Ordinal);
        Assert.Contains("Message startswith 'process:'", kusto, StringComparison.Ordinal);
        Assert.DoesNotContain("__", kusto, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trace_link_opens_the_workspace_s_logs_with_the_run_s_operation_in_its_query()
    {
        var reader = new InsightsProcessRuns(new AzureMetrics("client-1", configured: false), Workspace, "func-jobhunt-ss");

        string link = reader.TraceHref("op-1")!;

        Assert.StartsWith("https://portal.azure.com/#blade/Microsoft_OperationsManagementSuite_Workspace/Logs.ReactView/resourceId/", link, StringComparison.Ordinal);
        string packed = Uri.UnescapeDataString(link.Split("/q/")[1].Split('/')[0]);
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(packed)), CompressionMode.Decompress);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        Assert.Contains("OperationId == 'op-1'", text.ReadToEnd(), StringComparison.Ordinal);
        Assert.EndsWith("/timespan/P30D", reader.ProcessHref("Merger"), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_process_s_summary_is_read_from_its_own_file_in_the_build()
    {
        var descriptions = new ProcessDescriptions(new LiveCodeRegions(Repo.Root(), "local"));

        Assert.StartsWith("The job hunt's clock and its hands.", descriptions.Function(), StringComparison.Ordinal);
        Assert.StartsWith("Reads the inbox so Steve never sees a job-board alert", descriptions.For("AtsSweep"), StringComparison.Ordinal);
        foreach (var process in FunctionProcesses.All)
        {
            string summary = descriptions.For(process.Key);
            Assert.False(summary.StartsWith("not in this build", StringComparison.Ordinal), $"{process.Key}: {summary}");
            Assert.DoesNotContain("///", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("<summary>", summary, StringComparison.Ordinal);
        }
        Assert.StartsWith("not in this build", new ProcessDescriptions(new LiveCodeRegions(Path.GetTempPath(), "local")).For("Merger"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_health_route_s_switches_are_kept_by_the_six_processes_names_and_as_plain_words_only()
    {
        using var answer = JsonDocument.Parse("""{"waiting":0,"switches":{"ATS_SWEEP":"on","REPLY_ON_HUMAN":"SHADOW","SHEET_MIRROR":"https://x","SECRET_KEY":"on","MERGER":3}}""");

        var switches = NightlyProcesses.Switches(answer.RootElement)!;

        Assert.Equal(["ATS_SWEEP", "REPLY_ON_HUMAN"], switches.Keys.Order());
        Assert.Equal("shadow", switches["REPLY_ON_HUMAN"]);
        using var none = JsonDocument.Parse("""{"waiting":0}""");
        Assert.Null(NightlyProcesses.Switches(none.RootElement));
    }

    [Fact]
    [Covers("GET", "/api/admin/cloud/nightly/runs/{process}", 200)]
    [Covers("GET", "/api/admin/cloud/nightly/runs/{process}", 404)]
    public async Task The_runs_route_answers_a_page_for_each_of_the_six_and_a_404_problem_for_any_other_name()
    {
        using var client = factory.CreateClient();
        foreach (var process in FunctionProcesses.All)
        {
            var response = await client.GetAsync($"/api/admin/cloud/nightly/runs/{process.Key}?window=7d&page=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(process.Title, body.RootElement.GetProperty("title").GetString());
            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("rows").ValueKind);
            Assert.False(body.RootElement.GetProperty("available").GetBoolean());
        }
        var missing = await client.GetAsync("/api/admin/cloud/nightly/runs/Vault");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    [Covers("GET", "/api/admin/incidents", 200)]
    public async Task The_incidents_route_lists_every_incident_and_says_why_off_Azure_it_read_no_failures()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/admin/incidents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INC-001", body.RootElement.GetProperty("incidents")[0].GetProperty("id").GetString());
        Assert.False(body.RootElement.GetProperty("failures_available").GetBoolean());
        Assert.StartsWith("not read here", body.RootElement.GetProperty("failures_note").GetString(), StringComparison.Ordinal);
        Assert.Equal("unread", body.RootElement.GetProperty("tone").GetString());
    }

    [Fact]
    public async Task The_nightly_card_carries_its_description_and_seven_sections_and_the_other_cards_neither()
    {
        using var client = factory.CreateClient();

        using var nightly = JsonDocument.Parse(await client.GetStringAsync("/api/admin/cloud/nightly?window=24h"));
        Assert.StartsWith("The job hunt's clock and its hands.", nightly.RootElement.GetProperty("description").GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(7, nightly.RootElement.GetProperty("processes").GetArrayLength());

        using var sql = JsonDocument.Parse(await client.GetStringAsync("/api/admin/cloud/sqldb?window=24h"));
        Assert.Equal(JsonValueKind.Null, sql.RootElement.GetProperty("description").ValueKind);
        Assert.Equal(0, sql.RootElement.GetProperty("processes").GetArrayLength());
    }
}
// #endregion process-readers-tests
