using System.Net;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The run reader apart from Azure (ADR: The Nightly Function on the Admin tab, the addendum of
/// 1.0.4.15): a GitHub answer and a run log, and nothing else. GitHub's runs fill the card, the log
/// is the durable copy whose older runs are folded in, a refused read falls back to the log, and a
/// configured token is sent as a bearer token while none is sent without one.
/// </summary>
public sealed partial class NightlyCardTests
{
    /// <summary>GitHub alone: the status and body given, every other host a failure, and the authorization header kept.</summary>
    private sealed class GitHubOnly(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string?> Signed { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "api.github.com")
            {
                throw new InvalidOperationException($"the run reader asked {request.RequestUri.Host}, which is not GitHub");
            }
            Signed.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private const string TwoRuns = """
        {"workflow_runs":[
          {"id":21,"run_number":21,"event":"repository_dispatch","display_title":"nightly","status":"completed","conclusion":"success","created_at":"2026-10-20T09:00:02Z","run_started_at":"2026-10-20T09:00:05Z","updated_at":"2026-10-20T09:04:05Z"},
          {"id":22,"run_number":22,"event":"repository_dispatch","display_title":"bookkeeping","status":"completed","conclusion":"success","created_at":"2026-10-20T13:00:02Z","run_started_at":"2026-10-20T13:00:05Z","updated_at":"2026-10-20T13:03:05Z"}
        ]}
        """;

    // #region nightly-run-reader-tests
    [Fact]
    public void A_function_not_found_says_what_the_group_listed()
    {
        var names = new NightlyResources("RG-TEST-NIGHTLY", "func-test", "plan-test", "sttest", "owner/repo", "nightly.yml");
        Assert.Equal(
            "RG-TEST-NIGHTLY lists nothing to the site's identity: the group is empty, or it holds no role that reads it",
            NightlySource.Unfound([], names));
        AzureResource[] held = [new("/x/plan-test", "plan-test", "Microsoft.Web/serverfarms"), new("/x/func-test-2", "func-test-2", "Microsoft.Web/sites")];
        Assert.Equal(
            "no Microsoft.Web/sites named func-test in RG-TEST-NIGHTLY, which lists 2 resources and func-test-2",
            NightlySource.Unfound(held, names));
        Assert.EndsWith("which lists 1 resources and no web or Function app", NightlySource.Unfound([held[0]], names), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reader_fills_the_card_from_github_with_no_log_and_sends_a_token_only_when_one_is_set()
    {
        var github = new GitHubOnly(HttpStatusCode.OK, TwoRuns);
        using var route = OutboundHttp.RouteTo(github);
        var cache = new NightlyRunCache();
        var unsigned = new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), new UnkeptRunLog(), cache, new FixedClock(Now));
        Assert.Equal("2 runs read", await unsigned.ReadAsync(CancellationToken.None));
        Assert.Equal(["22", "21"], cache.Runs.Select(r => r.Id));
        await new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml", "t0k"), new UnkeptRunLog(), cache, new FixedClock(Now)).ReadAsync(CancellationToken.None);
        await new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml", "__placeholder__"), new UnkeptRunLog(), cache, new FixedClock(Now)).ReadAsync(CancellationToken.None);
        Assert.Equal(new string?[] { null, "Bearer t0k", null }, github.Signed);
    }

    [Fact]
    public async Task The_log_s_older_runs_are_folded_in_beside_github_s_and_a_refused_read_falls_back_to_the_log()
    {
        var log = new MemoryNightlyRunLog("kept in the test");
        await log.UpsertAsync([Run("9", 200, "failure", "bookkeeping")], CancellationToken.None);
        var cache = new NightlyRunCache();
        using (OutboundHttp.RouteTo(new GitHubOnly(HttpStatusCode.OK, TwoRuns)))
        {
            await new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, cache, new FixedClock(Now)).ReadAsync(CancellationToken.None);
        }
        Assert.Equal(["22", "21", "9"], cache.Runs.Select(r => r.Id));
        Assert.Equal(3, (await log.SinceAsync(Now.AddDays(-30), CancellationToken.None)).Count);

        var fallback = new NightlyRunCache();
        using (OutboundHttp.RouteTo(new GitHubOnly(HttpStatusCode.Forbidden, "{}")))
        {
            Assert.Equal("not read: GitHub answered 403", await new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, fallback, new FixedClock(Now)).ReadAsync(CancellationToken.None));
        }
        Assert.Equal(3, fallback.Runs.Count);
    }
    // #endregion nightly-run-reader-tests
}
