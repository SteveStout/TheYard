using System.Net;
using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The nightly card's merger row (ADR: The tracker merger runs in Azure): the Function's health
/// route read once a minute and kept on the minute, the row's three tones at five and ten minutes,
/// and the wire check: whatever else the Function's answer carries, only the five numbers reach the
/// card, so no file name, row or log line from the share can reach the site.
/// </summary>
public sealed partial class NightlyCardTests
{
    private const string FunctionHost = "func-test-h4sh.centralus-01.azurewebsites.net";

    /// <summary>
    /// The token, a group holding the Function, its record naming its host, Monitor and the shares
    /// answering nothing, GitHub listing no runs, and the Function's health route answering the body
    /// given: five numbers and, beside them, text the site must never keep.
    /// </summary>
    private sealed class FunctionGroup(string health) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Asked.Add(uri.Host + uri.AbsolutePath);
            string body = uri.Host == "169.254.169.254" ? """{"access_token":"e30.e30.sig"}"""
                : uri.Host == "api.github.com" ? """{"workflow_runs":[]}"""
                : uri.Host == FunctionHost ? health
                : uri.AbsolutePath.EndsWith("/resources", StringComparison.Ordinal) ? $$"""{"value":[{"id":"{{FunctionPath}}","name":"func-test","type":"Microsoft.Web/sites"}]}"""
                : string.Equals(uri.AbsolutePath, FunctionPath, StringComparison.OrdinalIgnoreCase) ? $$$"""{"location":"centralus","properties":{"state":"Running","defaultHostName":"{{{FunctionHost}}}"}}"""
                : """{"value":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>The nightly card as ResourceCatalog.Cards builds it, over a configured identity.</summary>
    private static ResourceCard CatalogueNightly()
    {
        var azure = new AzureMetrics("client-1", configured: true);
        var log = new UnkeptRunLog();
        var source = new NightlySource(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST-NIGHTLY", TimeProvider.System), Names,
            new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, new NightlyRunCache(), new FixedClock(Now)), log, FunctionPath);
        var site = new AzureResources("RG-TEST", "app-test", "sqldb-test", "cosmos-test", "crtest", "log-test", "acs-test", "edge-test", new Uri("https://yard.example"), new DateOnly(2026, 10, 30));
        return ResourceCatalog.Cards(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST", TimeProvider.System), new NetlifyEdge(null, "edge-test", new Uri("https://yard.example")), (3, "m1"), site, nightly: source)
            .Single(card => card.Key == "nightly");
    }

    /// <summary>Inputs whose last hour holds the minutes given and nothing else.</summary>
    private static ResourceCardInputs WithMinutes(IReadOnlyList<ResourceMinute> minutes) =>
        new("24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), minutes, [], [], [], null, "the test site");

    private static ResourceMinute Merger(double waiting, double over5, double over10, double? oldest, double? logAge = 30) =>
        new(Now.AddMinutes(-1), new Dictionary<string, double?>
        {
            [MergerReading.LogAge] = logAge,
            [MergerReading.Waiting] = waiting,
            [MergerReading.Over5] = over5,
            [MergerReading.Over10] = over10,
            [MergerReading.Oldest] = oldest,
        }, "1.0.4.27");

    // #region nightly-merger-tests
    [Fact]
    public async Task The_minute_carries_the_function_s_five_numbers_and_nothing_else_reaches_the_card()
    {
        const string health = """
            {"log_age_s":42,"waiting":2,"waiting_over_5":1,"waiting_over_10":1,"oldest_wait_s":700,
             "file":"20261022-150000Z-lane-a.csv","log":"2026-10-22 10:59:00 MERGED Job_Hunt_Tracker.csv","rows":[{"Person":"Ann"}]}
            """;
        var nightly = CatalogueNightly();
        var group = new FunctionGroup(health);
        IReadOnlyList<ResourceMinute> minutes;
        using (OutboundHttp.RouteTo(group))
        {
            await nightly.Hour(Now, Now.AddHours(-1), CancellationToken.None);
            minutes = await nightly.Minutes(Now.AddMinutes(-10), Now, "1.0.4.27", CancellationToken.None);
        }
        Assert.Contains(FunctionHost + "/api/merger/health", group.Asked);
        var kept = MergerReading.Newest(minutes);
        Assert.NotNull(kept);
        Assert.Equal(1d, kept.Values[MergerReading.Over10]);
        Assert.Equal(700d, kept.Values[MergerReading.Oldest]);
        Assert.All(minutes.SelectMany(m => m.Values.Keys), key => Assert.True(MergerReading.Fields.Values.Contains(key) || NightlyMetrics.Readings.Contains(key), $"{key} is kept and is not a reading"));

        var report = nightly.View(WithMinutes(minutes));
        var row = report.Rows.Single(r => r.Key == "merger");
        Assert.Equal(("1 file waiting over 10 minutes", "warning"), (row.Answer, row.Tone));
        string wire = JsonSerializer.Serialize(report);
        foreach (string share in new[] { "lane-a", ".csv", "Job_Hunt", "MERGED", "Ann" })
        {
            Assert.DoesNotContain(share, wire, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_first_minute_after_a_roll_reads_the_host_itself_and_reads_the_record_once()
    {
        // A roll starts a new process with no hour read yet: the merger row must not wait for the hour.
        var nightly = CatalogueNightly();
        var group = new FunctionGroup("""{"log_age_s":12,"waiting":0,"waiting_over_5":0,"waiting_over_10":0,"oldest_wait_s":null}""");
        IReadOnlyList<ResourceMinute> first, second;
        using (OutboundHttp.RouteTo(group))
        {
            first = await nightly.Minutes(Now.AddMinutes(-2), Now.AddMinutes(-1), "1.0.4.30", CancellationToken.None);
            second = await nightly.Minutes(Now.AddMinutes(-1), Now, "1.0.4.30", CancellationToken.None);
        }
        Assert.Equal(0d, MergerReading.Newest(first)?.Values[MergerReading.Waiting]);
        Assert.Equal(0d, MergerReading.Newest(second)?.Values[MergerReading.Waiting]);
        Assert.Equal(2, group.Asked.Count(asked => asked == FunctionHost + "/api/merger/health"));
        Assert.Equal(1, group.Asked.Count(asked => asked.EndsWith(FunctionPath, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(("keeping up", "good"), (NightlyRows.MergerRow(WithMinutes(first)).Answer, NightlyRows.MergerRow(WithMinutes(first)).Tone));
    }

    [Fact]
    public async Task A_record_that_refuses_the_minute_costs_the_merger_reading_and_nothing_else()
    {
        var nightly = CatalogueNightly();
        var group = new RefusedRecord();
        IReadOnlyList<ResourceMinute> minutes;
        using (OutboundHttp.RouteTo(group))
        {
            minutes = await nightly.Minutes(Now.AddMinutes(-2), Now, "1.0.4.30", CancellationToken.None);
        }
        Assert.Null(MergerReading.Newest(minutes));
        Assert.Contains(group.Asked, asked => asked.EndsWith("/metrics", StringComparison.Ordinal));
        Assert.DoesNotContain(group.Asked, asked => asked.StartsWith(FunctionHost, StringComparison.Ordinal));
        Assert.Equal(NightlyRows.MergerUnread, NightlyRows.MergerRow(WithMinutes(minutes)).Answer);
    }

    /// <summary>The same group, with the Function's own record answering 403 and Monitor answering an empty series.</summary>
    private sealed class RefusedRecord : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Asked.Add(uri.Host + uri.AbsolutePath);
            if (string.Equals(uri.AbsolutePath, FunctionPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
            }
            string body = uri.Host == "169.254.169.254" ? """{"access_token":"e30.e30.sig"}"""
                : uri.AbsolutePath.EndsWith("/resources", StringComparison.Ordinal) ? $$"""{"value":[{"id":"{{FunctionPath}}","name":"func-test","type":"Microsoft.Web/sites"}]}"""
                : """{"value":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData(0d, 0d, 0d, "keeping up", "good")]
    [InlineData(3d, 0d, 0d, "keeping up", "good")]
    [InlineData(3d, 2d, 0d, "2 files waiting over 5 minutes", "info")]
    [InlineData(3d, 2d, 1d, "1 file waiting over 10 minutes", "warning")]
    public void The_merger_row_is_good_until_five_minutes_and_a_warning_past_ten(double waiting, double over5, double over10, string answer, string tone)
    {
        var row = NightlyRows.MergerRow(WithMinutes([Merger(waiting, over5, over10, waiting > 0 ? 400 : null)]));
        Assert.Equal((answer, tone), (row.Answer, row.Tone));
        Assert.Equal("Is the tracker merger keeping up?", row.Question);
        Assert.StartsWith("newest merge.log line 30 s ago", row.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_hour_with_no_merger_reading_says_so_and_a_missing_log_is_named()
    {
        var unread = NightlyRows.MergerRow(WithMinutes([new ResourceMinute(Now.AddMinutes(-1), new Dictionary<string, double?> { ["executions"] = 1 }, "1.0.4.27")]));
        Assert.Equal((NightlyRows.MergerUnread, "info"), (unread.Answer, unread.Tone));
        var noLog = NightlyRows.MergerRow(WithMinutes([Merger(0, 0, 0, null, logAge: null)]));
        Assert.Equal("no merge.log on the share", noLog.Detail[0].Text);
        Assert.Equal((5d, 10d), CloudThresholds.MergerWait);
    }

    [Fact]
    public void Only_numbers_under_the_five_names_are_read_from_the_function_s_answer()
    {
        using var answer = JsonDocument.Parse("""{"waiting":"3","waiting_over_5":1,"oldest_wait_s":null,"extra":9}""");
        var readings = MergerReading.Readings(answer.RootElement);
        Assert.Equal(MergerReading.Fields.Values.Order(), readings.Keys.Order());
        Assert.Null(readings[MergerReading.Waiting]);
        Assert.Equal(1d, readings[MergerReading.Over5]);
        Assert.Null(readings[MergerReading.Oldest]);
        Assert.Equal(["59 s", "11 min", "2 h 5 min"], new[] { 59d, 700d, 7500d }.Select(MergerReading.Age));
    }
    // #endregion nightly-merger-tests
}
