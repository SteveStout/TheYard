using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The nightly card before its two containers exist (ADR: The Nightly Function on the Admin tab,
/// the addendum of 1.0.4.10): GitHub's runs fill the card whether or not the run log is there, the
/// recorder still reads an hourly card's hour with no keep to put it in, an empty run log reads as
/// unread rather than off, and every "not read" names its reason.
/// </summary>
public sealed partial class NightlyCardTests
{
    // #region nightly-unkept-tests
    /// <summary>The run log as the document store answers before its container exists: not available, and nothing kept.</summary>
    private sealed class UnkeptRunLog : INightlyRunLog
    {
        public int Writes { get; private set; }

        public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new PlanHistoryAvailability(false, "the plan container is not there yet; apply infra/cosmos/plan.json"));

        public Task UpsertAsync(IReadOnlyList<NightlyRun> runs, CancellationToken cancellation)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task UpsertFiringsAsync(IReadOnlyList<NightlyFiring> firings, CancellationToken cancellation)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NightlyRun>> SinceAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<NightlyRun>>([]);
    }

    /// <summary>The instance metadata token, a 403 from Resource Manager, and three runs from GitHub.</summary>
    private sealed class ThreeRuns : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.Host switch
            {
                "169.254.169.254" => Answer("""{"access_token":"e30.e30.sig"}"""),
                "api.github.com" => Answer("""
                    {"workflow_runs":[
                      {"id":11,"run_number":8,"event":"repository_dispatch","display_title":"nightly","status":"completed","conclusion":"success","created_at":"2026-10-20T09:00:02Z","run_started_at":"2026-10-20T09:00:05Z","updated_at":"2026-10-20T09:04:05Z"},
                      {"id":12,"run_number":9,"event":"repository_dispatch","display_title":"bookkeeping","status":"completed","conclusion":"success","created_at":"2026-10-20T13:00:02Z","run_started_at":"2026-10-20T13:00:05Z","updated_at":"2026-10-20T13:03:05Z"},
                      {"id":13,"run_number":10,"event":"repository_dispatch","display_title":"bookkeeping","status":"completed","conclusion":"success","created_at":"2026-10-20T17:00:02Z","run_started_at":"2026-10-20T17:00:05Z","updated_at":"2026-10-20T17:03:05Z"}
                    ]}
                    """),
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden),
            });

        private static HttpResponseMessage Answer(string json) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }

    private static readonly PlanHistoryAvailability NoKeep = new(false, "the nightly container is not there yet; apply infra/cosmos/plan.json");

    /// <summary>The card's inputs with no keep: no minutes, no buckets, and the one hour the recorder holds, or none.</summary>
    private static ResourceCardInputs Unkept(ResourceHour? hour) =>
        new("24h", TimeSpan.FromDays(1), Now, NoKeep, [], [], [], hour is null ? [] : [hour], null, "the test site");

    /// <summary>
    /// The live state on 21 October: neither container exists, the identity holds no role, and GitHub
    /// lists three runs. The hour still reads GitHub, the cache holds the three, nothing is written
    /// to the log, and the card's run log and gates are GitHub's.
    /// </summary>
    [Fact]
    public async Task With_no_run_log_container_the_card_lists_github_s_runs_and_reads_the_gates_from_them()
    {
        using var route = OutboundHttp.RouteTo(new ThreeRuns());
        var azure = new AzureMetrics("client-1", configured: true);
        var log = new UnkeptRunLog();
        var cache = new NightlyRunCache();
        var source = new NightlySource(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST-NIGHTLY", TimeProvider.System), Names,
            new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, cache, new FixedClock(Now)), log, FunctionPath);

        var hour = await source.HourAsync(Now, Now.AddHours(-1), CancellationToken.None);
        var view = NightlyCard.View(Unkept(hour), Names, FunctionPath, cache.Runs);

        Assert.Equal("3 runs read", hour.Facts["github"]);
        Assert.Equal(0, log.Writes);
        var runs = view.Rows.Single(r => r.Key == "runs");
        Assert.StartsWith("3 runs in 30 days", runs.Answer, StringComparison.Ordinal);
        Assert.Equal(3, runs.Table.Rows.Count);
        Assert.StartsWith("nightly on · bookkeeping on", view.Readout.Lines.Single(l => l.Label == "Gates").Value, StringComparison.Ordinal);
        Assert.Contains("the nightly container is not there yet; the card reads live until it is", view.Note!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An empty run log is unread: the gates are said "on" or "off" only of a list that was read.</summary>
    [Fact]
    public void An_empty_run_log_reads_the_gates_as_not_read_yet_never_off()
    {
        string Gates(ResourceCardInputs inputs, IReadOnlyList<NightlyRun> runs) => NightlyCard.View(inputs, Names, FunctionPath, runs).Readout.Lines.Single(l => l.Label == "Gates").Value;
        Assert.Equal("not read yet: no hour kept yet", Gates(Unkept(null), []));
        var refused = new ResourceHour(Now.AddHours(-1), "", null, new Dictionary<string, string?> { ["github"] = "not read: GitHub answered 403" }, []);
        Assert.Equal("not read yet: GitHub answered 403", Gates(Unkept(refused), []));
        Assert.StartsWith("nightly off · bookkeeping on", Gates(Month(0), [Run("5", 2, "success", "bookkeeping")]), StringComparison.Ordinal);
    }

    /// <summary>Every "not read" on the card says why: before the first hour, while the role is missing, and when GitHub refused.</summary>
    [Fact]
    public void Every_not_read_on_the_card_names_its_reason()
    {
        var bare = new Regex(@"not read(?!( yet)?:)", RegexOptions.IgnoreCase);
        var hours = new ResourceHour?[]
        {
            null,
            new(Now.AddHours(-1), "", null, new Dictionary<string, string?> { [NightlySource.RoleFact] = NightlySource.RoleMissing, ["github"] = "not read: GitHub answered 403" }, []),
            new(Now.AddHours(-1), "", null, new Dictionary<string, string?> { ["site"] = NightlySource.Unfound([new("/x/plan-test", "plan-test", "Microsoft.Web/serverfarms")], Names), ["github"] = "0 runs read" }, []),
        };
        foreach (var hour in hours)
        {
            var view = NightlyCard.View(Unkept(hour), Names, FunctionPath, []);
            // The spec line is short by design and its reason is the Spec line of the readout.
            Assert.Equal("FLEX CONSUMPTION · NOT READ", view.Spec);
            Assert.Contains(view.Readout.Lines, l => l.Label == "Spec");
            var said = new List<string>();
            said.AddRange(view.Dials.Select(d => d.Absolute));
            said.AddRange(view.Rows.SelectMany(r => r.Detail.Select(d => d.Text).Append(r.Answer)));
            said.AddRange(view.Readout.Lines.Select(l => l.Value));
            Assert.All(said, text => Assert.False(bare.IsMatch(text), $"a bare \"not read\": {text}"));
        }
        Assert.Contains("not read yet: the nightly container is not there yet", JsonSerializer.Serialize(NightlyCard.View(Unkept(null), Names, FunctionPath, [])), StringComparison.Ordinal);
    }

    /// <summary>
    /// The recorder with an hourly card whose keep is not there: the hour is read once in its hour
    /// and held for the reader, the minutes are never asked for, and nothing is appended.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_recorder_reads_an_hourly_card_s_hour_with_no_keep_and_skips_its_minutes(bool hourly)
    {
        int minutes = 0, hours = 0;
        var card = new ResourceCard("nightly", "nightly", "func-test", ["executions"],
            (_, _, _, _) => { minutes++; return Task.FromResult<IReadOnlyList<ResourceMinute>>([]); },
            (hour, _, _) => { hours++; return Task.FromResult(new ResourceHour(hour, "", null, new Dictionary<string, string?> { ["github"] = "3 runs read" }, [])); },
            inputs => NightlyCard.View(inputs, Names, FunctionPath, []), Hourly: hourly);
        var keep = new NoKeepHistory();
        var recorder = new CloudRecorder([(card, keep)], new BuildInfo("1.0.4.10", "test"), new FixedClock(Now), NullLogger<CloudRecorder>.Instance);

        await recorder.RecordOnceAsync("nightly", CancellationToken.None);
        await recorder.RecordOnceAsync("nightly", CancellationToken.None);

        Assert.Equal((0, 1, 0), (minutes, hours, keep.Appends));
        Assert.Equal("3 runs read", recorder.LiveHour("nightly")!.Facts["github"]);
        var reader = new CloudReader([(card, keep)], recorder, (_, _) => Task.FromResult<double?>(null), new FixedClock(Now));
        var report = await reader.ReadAsync("nightly", "24h", CancellationToken.None);
        Assert.Contains("the nightly container is not there yet", report.Note!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, hours);
    }

    /// <summary>A keep whose container is missing: not available, empty on every read, counting any write.</summary>
    private sealed class NoKeepHistory : IResourceHistory
    {
        public int Appends { get; private set; }

        public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(NoKeep);

        public Task AppendMinuteAsync(ResourceMinute minute, CancellationToken cancellation)
        {
            Appends++;
            return Task.CompletedTask;
        }

        public Task AppendHourAsync(ResourceHour hour, CancellationToken cancellation)
        {
            Appends++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResourceMinute>> NewestAsync(int count, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<ResourceMinute>>([]);

        public Task<IReadOnlyList<ResourceBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, IReadOnlyList<string> readings, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<ResourceBucket>>([]);

        public Task<IReadOnlyList<ResourceHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<ResourceHour>>([]);
    }
    // #endregion nightly-unkept-tests
}
