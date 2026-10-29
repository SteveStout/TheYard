using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The nightly card fed by a push (ADR: The Nightly Function on the Admin tab, the addendum of
/// 1.0.4.114): the workflow posts the run it is, the reader draws from the log while the pushed
/// runs are current and asks GitHub only to reconcile, and a slot that came due with nothing
/// pushed sends the hour back to GitHub, which is what the card says.
/// </summary>
public sealed partial class NightlyCardTests
{
    /// <summary>A clock the test moves, for the reconcile that comes once a day.</summary>
    private sealed class MovedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>Moves the clock forward.</summary>
        /// <param name="span">How far.</param>
        public void Pass(TimeSpan span) => _now += span;
    }

    // #region nightly-push-tests
    /// <summary>
    /// The hour reads the log and leaves GitHub alone while a run pushed since the newest slot came
    /// due is held, which retires the hourly read and its sixty-an-hour allowance.
    /// </summary>
    [Fact]
    public async Task The_hour_draws_from_the_log_while_the_pushed_runs_are_current_and_leaves_github_alone()
    {
        var log = new MemoryNightlyRunLog("kept in the test");
        var github = new GitHubOnly(System.Net.HttpStatusCode.OK, TwoRuns);
        using var route = OutboundHttp.RouteTo(github);
        var cache = new NightlyRunCache();
        var reader = new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, cache, new FixedClock(Now));

        // Nothing kept: the first hour reconciles, so GitHub is asked once and its runs are kept.
        Assert.Equal("2 runs read", await reader.ReadAsync(CancellationToken.None));
        Assert.Single(github.Signed);

        // A run pushed at this moment is newer than the next slot the schedule has due, so the hour
        // reads the log back and never asks GitHub.
        await log.UpsertAsync([Run("23", 0, "success")], CancellationToken.None);
        Assert.Equal("3 runs kept", await reader.ReadAsync(CancellationToken.None));
        Assert.Single(github.Signed);
        Assert.Equal(["23", "22", "21"], cache.Runs.Select(run => run.Id));
    }

    /// <summary>A slot that came due with nothing pushed since: the hour asks GitHub, as it did before the push.</summary>
    [Fact]
    public async Task A_slot_due_with_nothing_pushed_since_sends_the_hour_back_to_github()
    {
        var log = new MemoryNightlyRunLog("kept in the test");
        // The newest kept run is a day old, so every slot since has come due with nothing pushed.
        await log.UpsertAsync([Run("9", 24, "success")], CancellationToken.None);
        var github = new GitHubOnly(System.Net.HttpStatusCode.OK, TwoRuns);
        using var route = OutboundHttp.RouteTo(github);
        var reader = new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, new NightlyRunCache(), new FixedClock(Now));
        Assert.Equal("2 runs read", await reader.ReadAsync(CancellationToken.None));
        Assert.Single(github.Signed);
    }

    /// <summary>
    /// GitHub is still read once a day on a card whose runs are all pushed, because GitHub's own
    /// conclusion for a run can differ from the word the job had when it posted itself.
    /// </summary>
    [Fact]
    public async Task Github_is_read_once_a_day_even_while_every_run_is_pushed()
    {
        var log = new MemoryNightlyRunLog("kept in the test");
        var github = new GitHubOnly(System.Net.HttpStatusCode.OK, TwoRuns);
        using var route = OutboundHttp.RouteTo(github);
        var clock = new MovedClock(Now);
        var reader = new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, new NightlyRunCache(), clock);
        Assert.Equal("2 runs read", await reader.ReadAsync(CancellationToken.None));

        // An hour on, with this hour's run pushed: the log answers and GitHub is left alone.
        clock.Pass(TimeSpan.FromHours(1));
        await log.UpsertAsync([Run("24", -1, "success")], CancellationToken.None);
        Assert.Equal("3 runs kept", await reader.ReadAsync(CancellationToken.None));
        Assert.Single(github.Signed);

        // A day after the last reconcile, with the pushed runs still current: GitHub is read again.
        clock.Pass(TimeSpan.FromHours(24));
        await log.UpsertAsync([Run("25", -25, "success")], CancellationToken.None);
        Assert.Equal("2 runs read", await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(2, github.Signed.Count);
    }

    /// <summary>
    /// The workflow that ran posts the run it is, beside the usage report: the same key, the same
    /// if: always(), and a missing key or a refused post writes one summary line and never fails the run.
    /// </summary>
    [Fact]
    public void The_nightly_workflow_posts_the_run_it_is_and_never_fails_the_run_for_it()
    {
        string yaml = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "nightly.yml"));
        int from = yaml.IndexOf("# #region nightly-run-report", StringComparison.Ordinal);
        Assert.True(from >= 0, "nightly.yml has no nightly-run-report region: the run does not post itself, so the card's runs can only come from GitHub");
        int to = yaml.IndexOf("# #endregion nightly-run-report", from, StringComparison.Ordinal);
        Assert.True(to > from, "nightly.yml's nightly-run-report region is not closed");
        string region = yaml[from..to];
        Assert.Contains("/api/admin/cloud/nightly/run", region, StringComparison.Ordinal);
        Assert.Contains("secrets.THEYARD_ADMIN_KEY", region, StringComparison.Ordinal);
        Assert.Contains(AdminKey.HeaderName + ": $KEY", region, StringComparison.Ordinal);
        Assert.Contains("if: always()", region, StringComparison.Ordinal);
        // The outcome as the job knows it at this step; GitHub's own conclusion can still differ.
        Assert.Contains("${{ job.status }}", region, StringComparison.Ordinal);
        // Never fails the run: a missing key leaves on 0 and a refused post writes a summary line.
        Assert.Contains("exit 0", region, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", region, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 1", region, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of reading the posted run by GitHub's own rules: a run the workflow posted and the
    /// same run read from GitHub's list are one document, so the hourly reconcile replaces a pushed
    /// run with itself and the card cannot show one run twice or two slots for one night.
    /// </summary>
    [Fact]
    public void A_run_pushed_and_the_same_run_read_from_github_are_one_document()
    {
        const string OneRun = """
            {"workflow_runs":[
              {"id":40868458131,"run_number":4114,"event":"repository_dispatch","display_title":"nightly","status":"completed","conclusion":"success","created_at":"2026-10-20T13:00:02Z","run_started_at":"2026-10-20T13:00:02Z","updated_at":"2026-10-20T13:03:34Z"}
            ]}
            """;
        using var answer = JsonDocument.Parse(OneRun);
        var listed = Assert.Single(GitHubRuns.Parse(answer.RootElement));
        var (pushed, wrong) = NightlyRunRoutes.Read(new NightlyRunRoutes.NightlyRunWire(
            "40868458131", 4114, GitHubRuns.Dispatched, NightlySlots.Reply, listed.At, 212, "success"));
        Assert.Null(wrong);
        Assert.Equal(listed, pushed);
        Assert.Equal(("08:00", GitHubRuns.ByFunction, "reply", 212L), (pushed!.Slot, pushed.StartedBy, pushed.Lane, pushed.TookSeconds));
        Assert.Null(NightlyRunRules.Validate(pushed, listed.At.AddMinutes(4)));
    }

    /// <summary>
    /// A run started by hand, dry, carries the same lane and the same dry flag either way; and a
    /// dispatch type the lane table does not name reads "not named" on both paths, as it does today.
    /// </summary>
    [Fact]
    public void A_dry_run_by_hand_and_a_dispatch_the_lane_table_does_not_name_read_the_same_either_way()
    {
        var at = new DateTimeOffset(2026, 10, 20, 9, 0, 2, TimeSpan.Zero);
        var (dry, _) = NightlyRunRoutes.Read(new NightlyRunRoutes.NightlyRunWire(
            "40868458132", 4115, GitHubRuns.ByHandEvent, "bookkeeping" + GitHubRuns.DrySuffix, at, 61, "failure"));
        Assert.Equal((GitHubRuns.ByHand, "bookkeeping", true, "04:00"), (dry!.StartedBy, dry.Lane, dry.Dry, dry.Slot));
        var (unnamed, _) = NightlyRunRoutes.Read(new NightlyRunRoutes.NightlyRunWire(
            "40868458133", 4116, GitHubRuns.Dispatched, "leads", at, 61, "success"));
        Assert.Equal((GitHubRuns.NotNamed, false), (unnamed!.Lane, unnamed.Dry));
    }

    /// <summary>A body the route cannot make a run of comes back as the one sentence that stops it being one.</summary>
    [Fact]
    public void A_wire_with_no_event_or_no_instant_is_refused_before_the_domain_is_asked()
    {
        var noEvent = NightlyRunRoutes.Read(new NightlyRunRoutes.NightlyRunWire("40868458134", 1, "schedule", "nightly", DateTimeOffset.UnixEpoch, 1, "success"));
        Assert.Null(noEvent.Run);
        Assert.Equal("event is repository_dispatch or workflow_dispatch.", noEvent.Wrong);
        var noInstant = NightlyRunRoutes.Read(new NightlyRunRoutes.NightlyRunWire("40868458134", 1, GitHubRuns.Dispatched, "nightly", null, 1, "success"));
        Assert.Null(noInstant.Run);
        Assert.Equal("at is an ISO 8601 instant.", noInstant.Wrong);
    }

    /// <summary>The run's own rules, each refusing with the one sentence the route gives back.</summary>
    [Fact]
    public void The_rules_refuse_a_run_that_is_not_one_github_could_have_listed()
    {
        var good = Run("40868458131", 1, "success");
        Assert.Null(NightlyRunRules.Validate(good, Now));
        Assert.Equal("run is GitHub's own run id, a whole number.", NightlyRunRules.Validate(good with { Id = "the-fourth-run" }, Now));
        Assert.Equal("number is the run's number in the workflow, one or more.", NightlyRunRules.Validate(good with { Number = 0 }, Now));
        Assert.Equal("slot is HH:MM.", NightlyRunRules.Validate(good with { Slot = "4 AM" }, Now));
        Assert.Equal("started_by is 1 to 30 characters.", NightlyRunRules.Validate(good with { StartedBy = "" }, Now));
        Assert.Equal("lane is 1 to 30 lower-case letters or spaces.", NightlyRunRules.Validate(good with { Lane = "REPLY" }, Now));
        Assert.Equal("took_seconds is zero or more.", NightlyRunRules.Validate(good with { TookSeconds = -1 }, Now));
        Assert.Equal("took_seconds is a day or less.", NightlyRunRules.Validate(good with { TookSeconds = NightlyRunRules.LongestSeconds + 1 }, Now));
        Assert.Equal("came_to is 1 to 30 lower-case letters or underscores.", NightlyRunRules.Validate(good with { CameTo = "Success!" }, Now));
        Assert.Equal("at is in the future.", NightlyRunRules.Validate(good with { At = Now.Add(NightlyRunRules.Skew).AddMinutes(1) }, Now));
        Assert.Equal("at is more than seven days ago.", NightlyRunRules.Validate(good with { At = Now - NightlyRunRules.Oldest.Add(TimeSpan.FromMinutes(1)) }, Now));
        // A run still going carries no duration, and that is not a broken rule.
        Assert.Null(NightlyRunRules.Validate(good with { TookSeconds = null }, Now));
    }

    /// <summary>The handler with a fake log and no host: a run the rules admit is kept, one they refuse is not, and a log that can keep nothing says so instead of reporting a run kept.</summary>
    [Fact]
    public async Task The_handler_keeps_a_run_the_rules_admit_and_never_reports_one_the_log_could_not_keep()
    {
        var log = new MemoryNightlyRunLog("kept in the test");
        var handler = new KeepNightlyRunHandler(log, new FixedClock(Now));
        var run = Run("40868458131", 1, "success");
        var kept = await handler.HandleAsync(new KeepNightlyRunCommand(run), CancellationToken.None);
        Assert.True(kept.Kept);
        Assert.Equal(run, Assert.Single(await log.SinceAsync(Now.AddDays(-30), CancellationToken.None)));

        // Posting it again keeps one, because the document's id is GitHub's own.
        Assert.True((await handler.HandleAsync(new KeepNightlyRunCommand(run with { CameTo = "failure" }), CancellationToken.None)).Kept);
        Assert.Equal("failure", Assert.Single(await log.SinceAsync(Now.AddDays(-30), CancellationToken.None)).CameTo);

        var refused = await handler.HandleAsync(new KeepNightlyRunCommand(run with { Id = "the-fourth-run" }), CancellationToken.None);
        Assert.Equal(("run is GitHub's own run id, a whole number.", (string?)null, false), (refused.Wrong, refused.Unavailable, refused.Kept));

        var unkept = new UnkeptRunLog();
        var unavailable = await new KeepNightlyRunHandler(unkept, new FixedClock(Now)).HandleAsync(new KeepNightlyRunCommand(run), CancellationToken.None);
        Assert.Equal(((string?)null, "the plan container is not there yet; apply infra/cosmos/plan.json", false), (unavailable.Wrong, unavailable.Unavailable, unavailable.Kept));
        Assert.Equal(0, unkept.Writes);
    }

    /// <summary>The card says where this hour's runs came from, so a reader never has to guess whether a night was pushed or read.</summary>
    [Fact]
    public void The_run_log_row_says_whether_this_hour_read_the_push_or_github()
    {
        string Line(string? github) => NightlyCard
            .View(Month(0, facts: github is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["github"] = github }), Names, FunctionPath, [Run("3", 1, "success")])
            .Rows.Single(row => row.Key == "runs").Detail[1].Text;
        Assert.StartsWith("read from this site's own run log", Line("4 runs kept"), StringComparison.Ordinal);
        Assert.StartsWith("read from GitHub this hour", Line("4 runs read"), StringComparison.Ordinal);
        Assert.Equal("GitHub was asked this hour and did not answer, so these are the log's: GitHub answered 403", Line("not read: GitHub answered 403"));
        Assert.Equal("where they came from: no hour kept yet", Line(null));
    }
    // #endregion nightly-push-tests
}

/// <summary>
/// POST /api/admin/cloud/nightly/run through the host: 401 without the operator's key, 400 with the
/// one sentence that is wrong, 200 with the id the run is kept under, the same run twice kept once,
/// and 503 with the log's reason on a host whose run log cannot keep anything.
/// </summary>
/// <param name="host">The host with the operator's key set.</param>
public sealed class AdminEndpointsNightlyRunTests(AdminEndpointsNightlyRunTests.KeyedHost host) : IClassFixture<AdminEndpointsNightlyRunTests.KeyedHost>
{
    /// <summary>The route as it is mapped.</summary>
    private const string Route = "/api/admin/cloud/nightly/run";

    /// <summary>The host with the operator's key set.</summary>
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        /// <summary>The operator's key on this host.</summary>
        public const string Key = "the-nightly-run-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    /// <summary>A run log that can keep nothing, so the route's 503 is read and not assumed.</summary>
    private sealed class Unkept : INightlyRunLog
    {
        /// <summary>The reason the route gives back.</summary>
        public const string Reason = "the plan container is not there yet; apply infra/cosmos/plan.json";

        public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new PlanHistoryAvailability(false, Reason));

        public Task UpsertAsync(IReadOnlyList<NightlyRun> runs, CancellationToken cancellation) => throw new InvalidOperationException("a run was written to a log that said it could keep nothing");

        public Task UpsertFiringsAsync(IReadOnlyList<NightlyFiring> firings, CancellationToken cancellation) => Task.CompletedTask;

        public Task<IReadOnlyList<NightlyRun>> SinceAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<NightlyRun>>([]);
    }

    /// <summary>One run as the workflow posts it: GitHub's own facts for the run, and the word the job had at the step.</summary>
    private static object Document(string run, string cameTo = "success", string trigger = "repository_dispatch", string title = "nightly", DateTimeOffset? at = null) => new
    {
        run,
        number = 4114L,
        @event = trigger,
        title,
        at = (at ?? DateTimeOffset.UtcNow.AddMinutes(-4)).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        took_seconds = 212L,
        came_to = cameTo,
    };

    private async Task<HttpResponseMessage> Post(object body, string? key = KeyedHost.Key, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add(AdminKey.HeaderName, key);
        }
        return await (client ?? host.CreateClient()).SendAsync(request);
    }

    /// <summary>Without the key, and with a guess at it, the route refuses before it reads a body.</summary>
    [Covers("POST", Route, 401)]
    [Fact]
    public async Task Without_the_key_the_route_refuses_before_it_reads_the_body()
    {
        Assert.Equal(401, (int)(await Post(Document("40868458131"), key: null)).StatusCode);
        Assert.Equal(401, (int)(await Post(Document("40868458131"), key: "a-guess")).StatusCode);
        Assert.Equal(401, (int)(await Post("not a run", key: null)).StatusCode);
    }

    /// <summary>A run the rules refuse comes back as the one sentence that is wrong with it.</summary>
    [Covers("POST", Route, 400)]
    [Fact]
    public async Task A_run_the_rules_refuse_is_400_with_the_one_sentence_that_is_wrong()
    {
        var notARun = await Post(Document("the-fourth-run"));
        Assert.Equal(400, (int)notARun.StatusCode);
        Assert.Contains("run is GitHub's own run id, a whole number.", await notARun.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var notAnEvent = await Post(Document("40868458132", trigger: "schedule"));
        Assert.Equal(400, (int)notAnEvent.StatusCode);
        Assert.Contains("event is repository_dispatch or workflow_dispatch.", await notAnEvent.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var old = await Post(Document("40868458133", at: DateTimeOffset.UtcNow.AddDays(-8)));
        Assert.Equal(400, (int)old.StatusCode);
        Assert.Contains("at is more than seven days ago.", await old.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>A run is kept under GitHub's own id, with the slot, the lane and the words the card reads.</summary>
    [Covers("POST", Route, 200)]
    [Fact]
    public async Task A_run_is_kept_under_github_s_id_with_the_slot_and_the_lane_the_card_reads()
    {
        string id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var at = DateTimeOffset.UtcNow.AddMinutes(-6);
        var log = host.Services.GetRequiredService<INightlyRunLog>();
        if ((await log.AvailabilityAsync(CancellationToken.None)).Available is false)
        {
            // A suite booted on a store with no plan container says so, and never claims a run it did
            // not keep. The gate's Cosmos boot is one: the run log's container is not in the test
            // database, so the route answers 503 here and the kept run is held by the SQLite boot.
            Assert.Equal(503, (int)(await Post(Document(id, at: at))).StatusCode);
            return;
        }
        var first = await Post(Document(id, at: at));
        Assert.Equal(200, (int)first.StatusCode);
        using var answer = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(id, answer.RootElement.GetProperty("run").GetString());

        // Posting the same run again keeps one: the document's id is GitHub's own.
        Assert.Equal(200, (int)(await Post(Document(id, at: at))).StatusCode);
        var kept = await log.SinceAsync(at.AddHours(-1), CancellationToken.None);
        var mine = Assert.Single(kept, run => run.Id == id);
        Assert.Equal(
            (TheYard.Domain.Features.Admin.NightlySlots.Local(at).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), GitHubRuns.ByFunction, "reply", false, 212L, "success"),
            (mine.Slot, mine.StartedBy, mine.Lane, mine.Dry, mine.TookSeconds, mine.CameTo));
    }

    /// <summary>A run log that can keep nothing answers its own reason, and never reports a run kept.</summary>
    [Covers("POST", Route, 503)]
    [Fact]
    public async Task A_run_log_that_can_keep_nothing_is_503_with_its_own_reason()
    {
        using var unkept = host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<INightlyRunLog>(new Unkept())));
        var refused = await Post(Document("40868458134"), client: unkept.CreateClient());
        Assert.Equal(503, (int)refused.StatusCode);
        Assert.Contains(Unkept.Reason, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
