using System.Text.Json;
using JobHunt.Application.Features.Watch;
using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>W9 and W12 (selfheal1120 version 4) over the shapes the site's card routes and GitHub answered on 11/19, through a fake web port (RULE 79).</summary>
public sealed class WatchBeatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 15, 13, 0, TimeSpan.Zero);

    [Fact]
    public void W9_a_name_behind_main_fails_after_30_minutes_and_is_rolling_before()
    {
        var made = Now.AddMinutes(-40);
        Assert.True(WatchBeats.Site("b14a232", "b14a232", "b14a2325940b", made, "success", Now).Pass);
        Assert.False(WatchBeats.Site("637ddd1", "b14a232", "b14a2325940b", made, "success", Now).Pass);
        Assert.Null(WatchBeats.Site("637ddd1", "b14a232", "b14a2325940b", Now.AddMinutes(-5), null, Now).Pass);
        Assert.False(WatchBeats.Site("b14a232", "b14a232", "b14a2325940b", Now.AddMinutes(-5), "failure", Now).Pass);
        Assert.Null(WatchBeats.Site("b14a232", "b14a232", null, null, null, Now).Pass);
    }

    [Fact]
    public void W12_a_failed_or_blocked_newest_run_a_red_card_row_and_a_stopped_unit_fail()
    {
        Assert.False(WatchBeats.Process("CampaignSend", "failed", "08:00").Pass);
        Assert.False(WatchBeats.Process("FreshScan", "blocked", "09:00").Pass);
        Assert.True(WatchBeats.Process("AtsSweep", "success", "15:50").Pass);
        Assert.Null(WatchBeats.Process("Watch", null, null).Pass);
        Assert.False(WatchBeats.CardRow("merger", "bad", "not keeping up").Pass);
        Assert.True(WatchBeats.CardRow("firing", "good", "170 of 170 slots on time").Pass);
        Assert.False(WatchBeats.Unit("jobhunt-dispatch.service (user)", "inactive, dead").Pass);
        Assert.True(WatchBeats.Unit("jobhunt-canary.timer (user)", "active, waiting").Pass);
        Assert.True(WatchBeats.Unit("jobhunt-6am.service (user)", "inactive, dead").Pass);
        Assert.False(WatchBeats.Unit("jobhunt-6am.service (user)", "failed, failed").Pass);
    }

    [Fact]
    public void W6_a_slot_past_its_time_with_nothing_written_today_fails_and_one_not_due_passes()
    {
        var eight = new DateTime(2026, 11, 20, 8, 0, 0);
        var slots = WatchBeats.Slots(eight, eight.Date.AddHours(4).AddMinutes(30), null, eight.Date.AddHours(6), eight.Date.AddHours(-16));
        Assert.Equal([true, true, true, false], slots.Select(r => r.Pass));
        Assert.Equal("not due yet", slots[1].Number);
        Assert.Equal("nothing today", slots[3].Number);
        Assert.False(WatchBeats.Slots(eight.AddHours(3), eight.Date.AddHours(5), null, null, null)[0].Pass);
        Assert.False(WatchBeats.Slots(eight.AddHours(3), null, null, null, null)[1].Pass);
    }

    [Fact]
    public void W11_three_of_fifty_two_fails_and_a_route_that_does_not_answer_is_not_read()
    {
        Assert.False(WatchBeats.Review(52, 3).Pass);
        Assert.Equal("3 of 52 reached a second action", WatchBeats.Review(52, 3).Number);
        Assert.True(WatchBeats.Review(10, 4).Pass);
        Assert.Null(WatchBeats.Review(null, null).Pass);
    }

    [Fact]
    public async Task A_tick_reads_the_site_and_github_through_the_port_and_names_each_task()
    {
        using var s = new Scratch();
        var web = new FakeWeb
        {
            ["/api/admin/cloud/box"] = """{"read_age_seconds": 42, "rows": [{"key": "services", "tone": "good", "table": {"rows": [[{"text": "jobhunt-dispatch.service (user)"}, {"text": "inactive, dead"}], [{"text": "jobhunt-canary.timer (user)"}, {"text": "active, waiting"}]]}}]}""",
            ["theyard.stevenstout.biz/api/version"] = """{"version": "1.0.5.115", "commit": "b14a232"}""",
            ["theyard-cosmos.stevenstout.biz/api/version"] = """{"version": "1.0.5.114", "commit": "637ddd1"}""",
            ["/commits/main"] = """{"sha": "b14a2325940b556d", "commit": {"committer": {"date": "2026-11-20T14:00:00Z"}}}""",
            ["/actions/runs"] = """{"workflow_runs": [{"name": "CI", "status": "completed", "conclusion": "success"}]}""",
            ["/api/admin/cloud/nightly?"] = """{"rows": [{"key": "firing", "tone": "good", "answer": "170 of 170 slots on time"}, {"key": "runs", "tone": "info", "answer": "x"}]}""",
            ["/nightly/runs/CampaignSend"] = """{"rows": [{"at_ms": 2, "when": "08:00", "outcome": "failed"}, {"at_ms": 1, "when": "07:00", "outcome": "success"}]}""",
        };

        var result = await new WatchPass(s.Store, _ => null, new At(Now), web).RunAsync(WatchMode.Shadow, default);

        WatchResult Named(string name) => result.Results.Single(r => r.Name == name);
        Assert.False(Named("TheYard up").Pass);
        Assert.Contains("theyard-cosmos 637ddd1", Named("TheYard up").Number, StringComparison.Ordinal);
        Assert.True(Named("the Function: firing").Pass);
        Assert.False(Named("CampaignSend").Pass);
        Assert.Equal("failed at 08:00", Named("CampaignSend").Number);
        Assert.Null(Named("Merger").Pass);
        Assert.False(Named("jobhunt-dispatch.service").Pass);
        Assert.True(Named("jobhunt-canary.timer").Pass);
        Assert.Equal("0 min", Named("the box card's snapshot").Number);
        Assert.Equal(2, web.Asked.Count(url => url.StartsWith(WatchPass.Repo, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task W9_reads_CI_by_the_run_name_ci_yml_gives_it_and_not_a_run_named_for_another_commit()
    {
        // The fixture, heal-w9-theyard-up-20261126: both names and main on b9c4a0b and CI green, and W9 read "CI running"
        // because ci.yml names its run "CI <sha>" (its run-name, 1.0.5.97) where the watch looked for "CI" alone.
        const string Sha = "b9c4a0baedff71c50b069b5146021beb0f26de84";
        async Task<WatchResult> Site(string runs)
        {
            using var s = new Scratch();
            var web = new FakeWeb
            {
                ["theyard.stevenstout.biz/api/version"] = """{"version": "1.0.5.168", "commit": "b9c4a0b"}""",
                ["theyard-cosmos.stevenstout.biz/api/version"] = """{"version": "1.0.5.168", "commit": "b9c4a0b"}""",
                ["/commits/main"] = """{"sha": """ + $"\"{Sha}\"" + """, "commit": {"committer": {"date": "2026-11-20T14:00:00Z"}}}""",
                ["/actions/runs"] = runs,
            };
            var result = await new WatchPass(s.Store, _ => null, new At(Now), web).RunAsync(WatchMode.Shadow, default);
            return result.Results.Single(r => r.Name == "TheYard up");
        }

        var green = await Site($$"""{"workflow_runs": [{"name": "Deploy", "status": "completed", "conclusion": "success"}, {"name": "CI {{Sha}}", "status": "completed", "conclusion": "success"}]}""");
        Assert.True(green.Pass);
        Assert.Equal("theyard b9c4a0b, theyard-cosmos b9c4a0b, main b9c4a0b, CI success", green.Number);
        Assert.False((await Site($$"""{"workflow_runs": [{"name": "CI {{Sha}}", "status": "completed", "conclusion": "failure"}]}""")).Pass);
        var other = await Site("""{"workflow_runs": [{"name": "CI 959e02e1a2b3c4d5e6f708192a3b4c5d6e7f8091", "status": "completed", "conclusion": "success"}]}""");
        Assert.False(other.Pass);
        Assert.EndsWith("CI running", other.Number, StringComparison.Ordinal);
        Assert.True((await Site("""{"workflow_runs": [{"name": "CI", "status": "completed", "conclusion": "success"}]}""")).Pass);
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeWeb : Dictionary<string, string>, IWatchWeb
    {
        public List<string> Asked { get; } = [];

        public Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
        {
            Asked.Add(url);
            string? body = this.Where(pair => url.Contains(pair.Key, StringComparison.Ordinal)).OrderByDescending(pair => pair.Key.Length).Select(pair => pair.Value).FirstOrDefault();
            return Task.FromResult(body is null ? null : JsonDocument.Parse(body));
        }
    }
}
