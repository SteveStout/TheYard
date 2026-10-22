using System.Text.Json;
using TheYard.Api;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The nightly Function's card (ADR: The Nightly Function on the Admin tab) with recorded inputs:
/// both dials' tones at their lines, the sentence every Azure line prints while the site's identity
/// holds no role on the Function's group, the run log's tones, GitHub's list of runs as the card's
/// runs, the three cost lines, the Monday and Friday bands, and the snapshot count held to the
/// Function's own file.
/// </summary>
public sealed partial class NightlyCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);

    /// <summary>A group name for the tests, not the deployment's, so the sentence is shown to come from configuration.</summary>
    private static readonly NightlyResources Names = new("RG-TEST-NIGHTLY", "func-test", "plan-test", "sttest", "owner/repo", "nightly.yml");

    private const string FunctionPath = "/subscriptions/sub-1/resourceGroups/RG-TEST-NIGHTLY/providers/Microsoft.Web/sites/func-test";

    /// <summary>
    /// Ten days of five-minute buckets with one firing in each of the first <paramref name="onTime"/>
    /// due slots at the hour, and none for the rest, plus each bucket's execution units.
    /// </summary>
    private static ResourceCardInputs Month(int onTime, double unitsPerBucket = 0, IReadOnlyDictionary<string, string?>? facts = null, IReadOnlyDictionary<string, double?>? costs = null)
    {
        var start = Now.AddDays(-10);
        var due = TheYard.Domain.NightlySlots.Due(start, Now.AddHours(-1)).Take(onTime).Select(d => d.At).ToHashSet();
        var buckets = new List<ResourceBucket>();
        for (var at = start; at < Now; at = at.AddMinutes(5))
        {
            var sums = new Dictionary<string, double?> { ["executions"] = due.Contains(at) ? 1 : 0, ["units_mbms"] = unitsPerBucket };
            buckets.Add(new ResourceBucket(at, 5, sums, sums, sums, "1.0.4.8"));
        }
        var lastHour = Enumerable.Range(0, 5).Select(i => new ResourceMinute(Now.AddMinutes(i - 5), new Dictionary<string, double?> { ["executions"] = 0 }, "1.0.4.8")).ToList();
        var hours = new List<ResourceHour> { new(Now.AddHours(-1), "", null, facts ?? new Dictionary<string, string?>(), []) };
        return new ResourceCardInputs("7d", TimeSpan.FromDays(7), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), lastHour, buckets, buckets, hours, null, "the test site", costs);
    }

    private static int DueInMonth() => TheYard.Domain.NightlySlots.Due(Now.AddDays(-10), Now.AddHours(-1)).Count;

    private static NightlyRun Run(string id, int hoursAgo, string cameTo, string lane = "reply") =>
        new(id, long.Parse(id, System.Globalization.CultureInfo.InvariantCulture), Now.AddHours(-hoursAgo), "04:00", GitHubRuns.ByFunction, false, lane, 300, cameTo);

    // #region nightly-card-tests
    /// <summary>The firing dial is a floor read on its misses: 95 per cent on time is amber's edge, under 90 is red.</summary>
    [Fact]
    public void The_on_time_dial_turns_amber_under_ninety_five_and_red_under_ninety()
    {
        int due = DueInMonth();
        string Tone(int onTime) => NightlyCard.View(Month(onTime), Names, FunctionPath, []).Dials[0].Tone;
        Assert.Equal("good", Tone(due));
        Assert.Equal("warning", Tone((int)Math.Floor(due * 0.94)));
        Assert.Equal("alarm", Tone((int)Math.Floor(due * 0.89)));
        Assert.Equal((95d, 90d), (NightlyCard.View(Month(due), Names, FunctionPath, []).Dials[0].Amber, NightlyCard.View(Month(due), Names, FunctionPath, []).Dials[0].Red));
        Assert.Equal($"{due} of {due} slots on time", NightlyCard.View(Month(due), Names, FunctionPath, []).Rows.Single(r => r.Key == "firing").Answer);
    }

    /// <summary>The grant dial counts the month's units in gigabyte-seconds against the free grant, amber at 80 and red at 95.</summary>
    [Theory]
    [InlineData(70_000d, "good")]
    [InlineData(80_000d, "warning")]
    [InlineData(95_000d, "alarm")]
    public void The_grant_dial_turns_at_eighty_and_ninety_five_per_cent(double gbSeconds, string tone)
    {
        // Spread over the buckets since the first of the month, so the dial's sum is the figure asked.
        var monthStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        int inMonth = (int)((Now - (Now.AddDays(-10) > monthStart ? Now.AddDays(-10) : monthStart)).TotalMinutes / 5);
        var inputs = Month(0, gbSeconds * NightlyMetrics.MbMsPerGbSecond / inMonth);
        Assert.Equal(gbSeconds, NightlyCard.GbSecondsThisMonth(inputs)!.Value, 0);
        Assert.Equal(tone, NightlyCard.View(inputs, Names, FunctionPath, []).Dials[1].Tone);
    }

    [Fact]
    public void Without_a_role_on_the_group_every_azure_line_says_so_and_the_card_never_throws()
    {
        var view = NightlyCard.View(Month(3, facts: new Dictionary<string, string?> { [NightlySource.RoleFact] = NightlySource.RoleMissing }), Names, FunctionPath, [Run("100", 3, "success")]);
        const string NoRole = "not read: no role on RG-TEST-NIGHTLY";
        Assert.All(view.Dials, dial => Assert.Equal(NoRole, dial.Absolute));
        Assert.All(view.Dials, dial => Assert.Null(dial.Percent));
        Assert.Equal(NoRole, view.Rows.Single(r => r.Key == "firing").Answer);
        Assert.Equal(NoRole, view.Readout.Lines.Single(l => l.Label == "Last fired").Value);
        Assert.Contains("RG-TEST-NIGHTLY", view.Note, StringComparison.Ordinal);
        Assert.False(view.Available);
        // GitHub's runs need no role, so the run log still reads.
        Assert.Single(view.Rows.Single(r => r.Key == "runs").Table.Rows);
        Assert.Equal("https://portal.azure.com/#resource" + FunctionPath, view.Resource!.Href);
    }

    [Fact]
    public void The_run_log_warns_on_one_failed_run_and_alarms_on_two()
    {
        Assert.Equal("good", NightlyRows.RunsTone([Run("3", 1, "success"), Run("2", 5, "failure")]));
        Assert.Equal("warning", NightlyRows.RunsTone([Run("3", 1, "failure"), Run("2", 5, "success")]));
        Assert.Equal("alarm", NightlyRows.RunsTone([Run("3", 1, "failure"), Run("2", 5, "timed_out")]));
        Assert.Equal("info", NightlyRows.RunsTone([]));
        var row = NightlyCard.View(Month(0), Names, FunctionPath, [Run("3", 1, "failure"), Run("2", 5, "failure")]).Rows.Single(r => r.Key == "runs");
        Assert.Equal("alarm", row.Tone);
        Assert.Equal(["When (Central)", "Slot", "Started by", "Run", "Took", "Came to"], row.Table.Columns);
        Assert.Equal("GitHub Actions, nightly.yml runs", row.Source);
    }

    /// <summary>GitHub's answer as its API shapes it: a records run dispatched by the Function, a dry run by hand, an old run with no lane in its name, and one still running.</summary>
    [Fact]
    public void GitHub_s_runs_become_the_card_s_runs_with_the_lane_from_the_title()
    {
        using var answer = JsonDocument.Parse("""
            {"total_count":4,"workflow_runs":[
              {"id":18234567890,"run_number":12,"event":"repository_dispatch","display_title":"bookkeeping","status":"completed","conclusion":"success","created_at":"2026-10-20T17:00:04Z","run_started_at":"2026-10-20T17:00:10Z","updated_at":"2026-10-20T17:06:40Z"},
              {"id":18234567001,"run_number":11,"event":"workflow_dispatch","display_title":"reply (dry run)","status":"completed","conclusion":"failure","created_at":"2026-10-20T15:00:00Z","run_started_at":"2026-10-20T15:00:05Z","updated_at":"2026-10-20T15:00:50Z"},
              {"id":18234560000,"run_number":3,"event":"workflow_dispatch","display_title":"Nightly","status":"completed","conclusion":"cancelled","created_at":"2026-10-19T09:00:00Z","run_started_at":"2026-10-19T09:00:03Z","updated_at":"2026-10-19T09:01:03Z"},
              {"id":18234569999,"run_number":13,"event":"repository_dispatch","display_title":"nightly","status":"in_progress","conclusion":null,"created_at":"2026-10-20T18:00:02Z","run_started_at":"2026-10-20T18:00:06Z","updated_at":"2026-10-20T18:01:00Z"},
              {"run_number":1,"event":"push"}
            ]}
            """);
        var runs = GitHubRuns.Parse(answer.RootElement);
        Assert.Equal(["18234569999", "18234567890", "18234567001", "18234560000"], runs.Select(r => r.Id));
        var records = runs[1];
        Assert.Equal((12L, "bookkeeping", GitHubRuns.ByFunction, false, "success", 390L, "12:00"), (records.Number, records.Lane, records.StartedBy, records.Dry, records.CameTo, records.TookSeconds!.Value, records.Slot));
        Assert.Equal(("reply", GitHubRuns.ByHand, true, "failure"), (runs[2].Lane, runs[2].StartedBy, runs[2].Dry, runs[2].CameTo));
        Assert.Equal((GitHubRuns.NotNamed, false), (runs[3].Lane, runs[3].Dry));
        Assert.Equal(("reply", "in_progress", (long?)null), (runs[0].Lane, runs[0].CameTo, runs[0].TookSeconds));
        using var empty = JsonDocument.Parse("{}");
        Assert.Empty(GitHubRuns.Parse(empty.RootElement));
        var address = new GitHubRuns("owner/repo", "nightly.yml").AddressAt(Now);
        Assert.Equal("/repos/owner/repo/actions/workflows/nightly.yml/runs", address.AbsolutePath);
        Assert.EndsWith("2026-09-20", Uri.UnescapeDataString(address.Query), StringComparison.Ordinal);
        Assert.Contains("created=>=", Uri.UnescapeDataString(address.Query), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cost_row_names_the_function_its_plan_and_its_storage_each_on_its_own_line()
    {
        var costs = new Dictionary<string, double?> { ["func-test"] = 0.01, ["plan-test"] = null, ["sttest"] = 0.42 };
        var row = NightlyCard.View(Month(0, costs: costs), Names, FunctionPath, []).Rows.Single(r => r.Key == "cost");
        Assert.Equal("$0.43 this month", row.Answer);
        Assert.Equal(["func-test: $0.01", "plan-test: not on the bill yet", "sttest: $0.42"], row.Detail.Take(3).Select(d => d.Text));
        Assert.StartsWith("share snapshots kept: not read", row.Detail[3].Text, StringComparison.Ordinal);
        Assert.Equal(["func-test", "plan-test", "sttest"], Names.CostNames);
        Assert.Equal("not on the bill yet", NightlyCard.View(Month(0), Names, FunctionPath, []).Rows.Single(r => r.Key == "cost").Answer);
    }

    [Fact]
    public void Every_monday_and_friday_in_the_window_is_a_band_in_central_days()
    {
        var bands = NightlyCard.Bands(Month(0));
        // The week before the fixed clock, a Tuesday at 13:00 Central, holds one Friday and one Monday.
        Assert.Equal(["Friday", "Monday"], bands.Select(b => b.Title));
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bands[1].StartMs);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 5, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bands[1].EndMs);
    }

    [Fact]
    public void The_allowed_row_says_no_file_in_the_share_is_opened_and_the_card_draws_in_the_frame()
    {
        var view = NightlyCard.View(Month(DueInMonth()), Names, FunctionPath, [Run("1", 2, "success")]);
        Assert.Contains(view.Rows.Single(r => r.Key == "allowed").Detail, d => d.Text == "No file in the share is opened here: the card sees the clock, the metrics, the share's snapshot list from Resource Manager, the merger's counts and ages from the Function, the public workflow runs and the bill.");
        Assert.Contains("Storage File Data SMB Share Contributor", view.Rows.Single(r => r.Key == "allowed").Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal(["firing", "merger", "runs", "cost", "allowed"], view.Rows.Select(r => r.Key));
        Assert.Equal(2, view.Dials.Count);
        Assert.Equal("The nightly job", view.Readout.Title);
        Assert.Contains("Central", view.Readout.Lines.Single(l => l.Label == "Next slot").Value, StringComparison.Ordinal);
        Assert.Equal("#1, reply, 5 min 0 s, success", view.Readout.Lines.Single(l => l.Label == "Last run on GitHub").Value);
        Assert.StartsWith("nightly on · bookkeeping off", view.Readout.Lines.Single(l => l.Label == "Gates").Value, StringComparison.Ordinal);
        Assert.DoesNotContain("job hunt", JsonSerializer.Serialize(view), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lane", JsonSerializer.Serialize(view), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The hour read through its real request building, against recorded answers: Resource Manager
    /// refuses the site's identity with a 403, as it does until the role is granted, and GitHub
    /// answers two runs. The hour says the role is missing instead of throwing, the runs are kept
    /// once however often they are read, and the card's cache holds them newest first.
    /// </summary>
    [Fact]
    public async Task A_refused_group_writes_role_missing_and_the_runs_are_still_read_and_kept_once()
    {
        using var route = OutboundHttp.RouteTo(new RefusedGroup());
        var azure = new AzureMetrics("client-1", configured: true);
        var log = new MemoryNightlyRunLog("no document store in the test");
        var cache = new NightlyRunCache();
        var source = new NightlySource(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST-NIGHTLY", TimeProvider.System), Names,
            new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, cache, new FixedClock(Now)), log, FunctionPath);

        var hour = await source.HourAsync(Now, Now.AddHours(-1), CancellationToken.None);
        await source.HourAsync(Now, Now.AddHours(-1), CancellationToken.None);

        Assert.Equal(NightlySource.RoleMissing, hour.Facts[NightlySource.RoleFact]);
        Assert.Equal("2 runs read", hour.Facts["github"]);
        Assert.Empty(await source.MinutesAsync(Now.AddMinutes(-5), Now, "1.0.4.8", CancellationToken.None));
        Assert.Equal(["2", "1"], cache.Runs.Select(r => r.Id));
        Assert.Equal(2, (await log.SinceAsync(Now.AddDays(-30), CancellationToken.None)).Count);
    }

    /// <summary>The instance metadata token, a 403 from Resource Manager, and two runs from GitHub.</summary>
    private sealed class RefusedGroup : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.Host switch
            {
                "169.254.169.254" => Answer("""{"access_token":"e30.e30.sig"}"""),
                "api.github.com" => Answer("""
                    {"workflow_runs":[
                      {"id":1,"run_number":1,"event":"repository_dispatch","display_title":"nightly","status":"completed","conclusion":"success","created_at":"2026-10-19T09:00:02Z","run_started_at":"2026-10-19T09:00:05Z","updated_at":"2026-10-19T09:04:05Z"},
                      {"id":2,"run_number":2,"event":"repository_dispatch","display_title":"bookkeeping","status":"completed","conclusion":"success","created_at":"2026-10-20T13:00:02Z","run_started_at":"2026-10-20T13:00:05Z","updated_at":"2026-10-20T13:03:05Z"}
                    ]}
                    """),
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden),
            });

        private static HttpResponseMessage Answer(string json) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The card does not reference the Function's project, so the count it prints is held to ShareSnapshot.Keep by reading the file.</summary>
    [Fact]
    public void The_snapshot_count_is_the_one_the_function_keeps()
    {
        string snapshot = File.ReadAllText(Path.Combine(Repo.Root(), "functions", "jobhunt", "ShareSnapshot.cs"));
        var keep = System.Text.RegularExpressions.Regex.Match(snapshot, @"public const int Keep = (\d+);");
        Assert.True(keep.Success, "ShareSnapshot.cs should declare Keep");
        Assert.Equal(NightlyCard.SnapshotsKept, int.Parse(keep.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
    // #endregion nightly-card-tests
}
