using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Api.Features.Admin.Operations;

namespace TheYard.Tests;

/// <summary>
/// A usage document's rules (ADR: Every Claude lane reports its usage): each sentence Validate
/// answers, the rules held by the Domain where the endpoint and the client both reach them.
/// </summary>
public sealed class UsageRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    /// <summary>A document every rule admits: the nightly lane's 4:00 AM run.</summary>
    public static UsageRecord Good(DateTimeOffset at, string run = "18745020001", string? mode = "reply", long tokensOut = 40_200, string lane = ClaudeCard.NightlyLane, decimal? cost = 4.08m, long wallMs = 616_000, string? slot = "04:00") =>
        new(lane, UsageKind.Plan, "claude-opus-5-5", 136, 163_711, 7_966_901, tokensOut, wallMs, cost, run, slot, mode, at);

    [Theory]
    [InlineData("lane", "lane is 1 to 40 letters, digits, spaces, commas or dashes.")]
    [InlineData("lane-long", "lane is 1 to 40 letters, digits, spaces, commas or dashes.")]
    [InlineData("model", "model is 1 to 80 letters, digits, dots, dashes or underscores.")]
    [InlineData("count", "every count is zero or more.")]
    [InlineData("cost", "cost_usd is zero or more.")]
    [InlineData("run", "run is 1 to 80 letters, digits, dots, dashes or underscores.")]
    [InlineData("slot", "slot is HH:MM.")]
    [InlineData("mode", "mode is 1 to 30 lower-case letters or dashes.")]
    [InlineData("future", "at is in the future.")]
    [InlineData("old", "at is more than seven days ago.")]
    [InlineData("good", null)]
    public void Each_rule_answers_its_own_sentence(string broken, string? said)
    {
        var good = Good(Now.AddHours(-12));
        var record = broken switch
        {
            "lane" => good with { Lane = "Job_Hunt_Tracker.csv" },
            "lane-long" => good with { Lane = new string('a', 41) },
            "model" => good with { Model = "" },
            "count" => good with { TokensOut = -1 },
            "cost" => good with { CostUsd = -0.01m },
            "run" => good with { Run = "nightly/2026-10-22/0400-reply" },
            "slot" => good with { Slot = "4 AM" },
            "mode" => good with { Mode = "Reply run" },
            "future" => good with { At = Now.AddMinutes(6) },
            "old" => good with { At = Now.AddDays(-8) },
            _ => good,
        };
        Assert.Equal(said, record.Validate(Now));
    }

    [Fact]
    public void A_dry_run_is_known_by_its_mode_and_the_day_is_the_utc_day()
    {
        Assert.True(Good(Now, mode: "bookkeeping-dry").Dry);
        Assert.False(Good(Now, mode: "bookkeeping").Dry);
        Assert.False(Good(Now, mode: null).Dry);
        Assert.Equal("2026-10-23", Good(new DateTimeOffset(2026, 10, 22, 23, 30, 0, TimeSpan.FromHours(-5))).Day);
    }
}

/// <summary>The usage log in memory: a document posted twice is kept once, and a read starts where it is asked to.</summary>
public sealed class UsageLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_document_posted_twice_is_kept_once_and_the_read_starts_where_asked()
    {
        var log = new MemoryUsageLog("kept in the test");
        await log.UpsertAsync(UsageRecordTests.Good(Now.AddDays(-2), run: "1"), CancellationToken.None);
        await log.UpsertAsync(UsageRecordTests.Good(Now.AddHours(-1), run: "2", tokensOut: 1), CancellationToken.None);
        await log.UpsertAsync(UsageRecordTests.Good(Now.AddHours(-1), run: "2", tokensOut: 2), CancellationToken.None);
        await log.UpsertAsync(UsageRecordTests.Good(Now.AddHours(-1), run: "2", lane: "Personal"), CancellationToken.None);

        var all = await log.SinceAsync(Now.AddDays(-3), CancellationToken.None);
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Single(record => record.Lane == ClaudeCard.NightlyLane && record.Run == "2").TokensOut);
        Assert.Equal(2, (await log.SinceAsync(Now.AddDays(-1), CancellationToken.None)).Count);
        Assert.Equal("Personal|2", MemoryUsageLog.KeyOf(all.Single(record => record.Lane == "Personal")));
    }

    [Fact]
    public async Task The_handler_keeps_a_document_the_rules_admit_and_gives_back_the_sentence_otherwise()
    {
        var log = new MemoryUsageLog("kept in the test");
        var keep = new TheYard.Application.Features.Admin.Operations.KeepUsageHandler(log, new Fixed(Now));
        Assert.True((await keep.HandleAsync(new TheYard.Application.Features.Admin.Operations.KeepUsageCommand(UsageRecordTests.Good(Now.AddHours(-2), run: "kept")), CancellationToken.None)).Kept);
        Assert.Equal("at is more than seven days ago.", (await keep.HandleAsync(new TheYard.Application.Features.Admin.Operations.KeepUsageCommand(UsageRecordTests.Good(Now.AddDays(-8), run: "old")), CancellationToken.None)).Wrong);
        Assert.Equal(["kept"], (await log.SinceAsync(Now.AddDays(-30), CancellationToken.None)).Select(record => record.Run));
    }

    [Fact]
    public async Task A_log_that_cannot_keep_says_why_and_nothing_is_reported_kept()
    {
        var keep = new TheYard.Application.Features.Admin.Operations.KeepUsageHandler(new Unkept(), new Fixed(Now));
        var said = await keep.HandleAsync(new TheYard.Application.Features.Admin.Operations.KeepUsageCommand(UsageRecordTests.Good(Now.AddHours(-2), run: "kept")), CancellationToken.None);
        Assert.Equal((false, null, "the plan container is not there yet"), (said.Kept, said.Wrong, said.Unavailable));
    }

    private sealed class Unkept : IUsageLog
    {
        public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new PlanHistoryAvailability(false, "the plan container is not there yet"));

        public Task UpsertAsync(UsageRecord record, CancellationToken cancellation) => throw new InvalidOperationException("an unkept log is never written");

        public Task<IReadOnlyList<UsageRecord>> SinceAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<UsageRecord>>([]);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// POST /api/admin/usage through the host: 401 without the key, 400 with the Domain's sentence, 200
/// with the key the document is kept under, the same document twice kept once, and a document eight
/// days old refused.
/// </summary>
public sealed class AdminEndpointsUsageTests(AdminEndpointsUsageTests.KeyedHost host) : IClassFixture<AdminEndpointsUsageTests.KeyedHost>
{
    /// <summary>The host with the operator's key set.</summary>
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        /// <summary>The operator's key on this host.</summary>
        public const string Key = "the-usage-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private static object Document(DateTimeOffset at, string run, string kind = "plan") => new
    {
        lane = "Personal",
        kind,
        model = "claude-opus-5-5",
        tokens_in = 10,
        cache_write = 20,
        cache_read = 30,
        tokens_out = 40,
        wall_ms = 5_000,
        cost_usd = 0.12m,
        run,
        slot = (string?)null,
        mode = (string?)null,
        at = at.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
    };

    private async Task<HttpResponseMessage> Post(object body, string? key = KeyedHost.Key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/usage") { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add(AdminKey.HeaderName, key);
        }
        return await host.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Without_the_key_the_route_refuses_before_it_reads_the_body()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(Document(DateTimeOffset.UtcNow, "personal-a"), key: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(Document(DateTimeOffset.UtcNow, "personal-a"), key: "a-guess")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("not a document", key: null)).StatusCode);
    }

    [Fact]
    public async Task A_broken_document_is_400_with_its_sentence_and_an_old_one_is_refused()
    {
        var wrongKind = await Post(Document(DateTimeOffset.UtcNow, "personal-b", kind: "subscription"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongKind.StatusCode);
        Assert.Contains("kind is plan or api.", await wrongKind.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var old = await Post(Document(DateTimeOffset.UtcNow.AddDays(-8), "personal-c"));
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Contains("at is more than seven days ago.", await old.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_is_kept_under_its_lane_and_run_and_posting_it_again_keeps_one()
    {
        string run = "personal-" + Guid.NewGuid().ToString("N")[..8];
        var log = host.Services.GetRequiredService<IUsageLog>();
        if ((await log.AvailabilityAsync(CancellationToken.None)).Available is false)
        {
            // A suite booted on a store with no plan container says so, and never claims a document it did not keep.
            var refused = await Post(Document(DateTimeOffset.UtcNow.AddMinutes(-3), run));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            return;
        }
        var first = await Post(Document(DateTimeOffset.UtcNow.AddMinutes(-3), run));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var answer = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal("Personal|" + run, answer.RootElement.GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Post(Document(DateTimeOffset.UtcNow.AddMinutes(-3), run))).StatusCode);

        var kept = await log.SinceAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);
        var mine = Assert.Single(kept, record => record.Run == run);
        Assert.Equal((UsageKind.Plan, 40L, 30L), (mine.Kind, mine.TokensOut, mine.CacheRead));
    }

    [Fact]
    public void The_wire_reads_kind_and_at_before_the_domain_is_asked()
    {
        var wire = new AdminUsageRoutes.UsageWire("Personal", "api", "m", 1, 2, 3, 4, 5, null, "r", null, null, null);
        var unread = AdminUsageRoutes.Read(wire);
        Assert.Null(unread.Record);
        Assert.Equal("at is an ISO 8601 instant.", unread.Wrong);
        var read = AdminUsageRoutes.Read(wire with { At = DateTimeOffset.UnixEpoch });
        Assert.Equal(UsageKind.Api, read.Record!.Kind);
    }
}
