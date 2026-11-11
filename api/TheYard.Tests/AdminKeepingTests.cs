using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The plan card, the Azure card and the runs route answer from a view already built, and build the next behind
/// the request, the way the Cloud cards do (ADR: One frame for every resource, the addendum that gave these three
/// the same keeping). A 48-hour probe of the Admin routes found the plan built on every one of about 280 reads, at
/// a p50 of 2.9 s and a p95 of 7.9 to 13.7 s, the Azure card at a cold p95 of 4.3 to 4.5 s and the runs route at
/// 1.7 s. Each build here after the first is parked on a gate the test opens, so a case can say the request did not
/// wait on it rather than only that it was quick.
/// </summary>
public sealed class AdminKeepingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);

    private static readonly FunctionProcess Merger = FunctionProcesses.Find("Merger")!;

    // #region plan
    /// <summary>A plan reader with no recorder, whose every build after the first waits on <paramref name="gate"/>; <paramref name="builds"/> counts the builds begun.</summary>
    private static PlanHistoryReader Plan(MovableClock clock, TaskCompletionSource gate, Func<int> builds) =>
        new(new MemoryPlanHistory("kept in the test"), null, async cancellation =>
        {
            if (builds() > 1)
            {
                await gate.Task.WaitAsync(cancellation);
            }
            return null;
        }, new KeptViews<CloudReport>("plan card", clock));

    [Fact]
    public async Task The_plan_a_minute_old_answers_at_once_and_its_next_is_built_behind_it()
    {
        var clock = new MovableClock(Start);
        var gate = new TaskCompletionSource();
        int built = 0;
        var plan = Plan(clock, gate, () => ++built);
        var first = await plan.ReadAsync("24h", CancellationToken.None);
        clock.Now = Start.AddSeconds(90);

        var read = plan.ReadAsync("24h", CancellationToken.None);
        var again = plan.ReadAsync("24h", CancellationToken.None);

        Assert.True(read.IsCompleted && again.IsCompleted, "the request waited on the build behind it");
        Assert.Equal(first.AsOfMs, (await read).AsOfMs);
        gate.SetResult();
        await plan.Views.BuiltBehind();
        Assert.Equal(2, built);
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (await plan.ReadAsync("24h", CancellationToken.None)).AsOfMs);
    }

    [Fact]
    public async Task The_keeper_builds_the_plan_window_asked_for_this_hour_and_drops_one_nobody_asked_for()
    {
        var clock = new MovableClock(Start);
        var gate = new TaskCompletionSource();
        gate.SetResult();
        int built = 0;
        var plan = Plan(clock, gate, () => ++built);
        await plan.ReadAsync("7d", CancellationToken.None);

        await plan.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(1, built);
        clock.Now = Start.AddSeconds(61);
        await plan.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(2, built);
        clock.Now = Start + KeptViews.Watched + TimeSpan.FromMinutes(1);
        await plan.Views.KeepWatchedAsync(CancellationToken.None);

        Assert.Equal(2, built);
    }
    // #endregion plan

    // #region azure
    private const string SelfPath = "/subscriptions/s/resourceGroups/RG/providers/Microsoft.Web/sites/app-theyard-ss";

    [Fact]
    public async Task The_Azure_card_a_minute_old_answers_at_once_and_its_next_is_built_behind_it()
    {
        var clock = new MovableClock(Start);
        var azure = new GatedAzure();
        using var route = OutboundHttp.RouteTo(azure);
        var self = new AzureSelf("client-1", SelfPath, null, clock);
        var first = await self.GetStateAsync(CancellationToken.None);
        clock.Now = Start.AddSeconds(90);

        var read = self.GetStateAsync(CancellationToken.None);
        var again = self.GetStateAsync(CancellationToken.None);

        Assert.True(read.IsCompleted && again.IsCompleted, "the request waited on the build behind it");
        Assert.Same(first, await read);
        azure.Gate.SetResult();
        await self.Views.BuiltBehind();
        Assert.Equal(2, azure.SiteReads);
        Assert.NotSame(first, await self.GetStateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_keeper_builds_the_Azure_card_asked_for_this_hour_and_a_refused_read_keeps_the_view_it_had()
    {
        var clock = new MovableClock(Start);
        var azure = new GatedAzure();
        azure.Gate.SetResult();
        using var route = OutboundHttp.RouteTo(azure);
        var self = new AzureSelf("client-1", SelfPath, null, clock);
        await self.GetStateAsync(CancellationToken.None);

        clock.Now = Start.AddSeconds(61);
        await self.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(2, azure.SiteReads);
        azure.Refuse = true;
        clock.Now = Start.AddSeconds(122);
        await self.Views.KeepWatchedAsync(CancellationToken.None);
        var held = await self.GetStateAsync(CancellationToken.None);
        await self.Views.BuiltBehind();
        Assert.Equal(4, azure.SiteReads);
        Assert.True(System.Text.Json.JsonSerializer.SerializeToNode(held)!["available"]!.GetValue<bool>());
        clock.Now = Start + KeptViews.Watched + TimeSpan.FromMinutes(5);
        await self.Views.KeepWatchedAsync(CancellationToken.None);

        Assert.Equal(4, azure.SiteReads);
    }

    /// <summary>Azure for the card: a token, the site, and nothing for its configuration or plan; every site read after the first waits on <see cref="Gate"/>.</summary>
    private sealed class GatedAzure : HttpMessageHandler
    {
        private int _siteReads;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Refuse { get; set; }

        public int SiteReads => Volatile.Read(ref _siteReads);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri!.ToString();
            if (!uri.StartsWith("https://management.azure.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"t"}""") };
            }
            if (!request.RequestUri.AbsolutePath.EndsWith("/sites/app-theyard-ss", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (Interlocked.Increment(ref _siteReads) > 1)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }
            return Refuse
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"location":"centralus","properties":{"state":"Running"}}""") };
        }
    }
    // #endregion azure

    // #region runs
    [Fact]
    public async Task A_runs_page_a_minute_old_answers_at_once_and_its_next_is_built_behind_it()
    {
        var clock = new MovableClock(Start);
        var inner = new GatedRuns();
        var runs = new GuardedProcessRuns(inner, clock);
        var first = await runs.RunsAsync(Merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);
        clock.Now = Start.AddSeconds(90);

        var read = runs.RunsAsync(Merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);
        var again = runs.RunsAsync(Merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None);

        Assert.True(read.IsCompleted && again.IsCompleted, "the request waited on the build behind it");
        Assert.Same(first, await read);
        inner.Gate.SetResult();
        await runs.Views.BuiltBehind();
        Assert.Equal(2, inner.Reads);
        Assert.NotSame(first, await runs.RunsAsync(Merger, TimeSpan.FromDays(7), 0, 20, CancellationToken.None));
    }

    [Fact]
    public async Task The_keeper_builds_the_runs_page_asked_for_this_hour_and_drops_one_nobody_asked_for()
    {
        var clock = new MovableClock(Start);
        var inner = new GatedRuns();
        inner.Gate.SetResult();
        var runs = new GuardedProcessRuns(inner, clock);
        await runs.RunsAsync(Merger, TimeSpan.FromDays(1), 0, 20, CancellationToken.None);

        await runs.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(1, inner.Reads);
        clock.Now = Start.AddSeconds(61);
        await runs.Views.KeepWatchedAsync(CancellationToken.None);
        Assert.Equal(2, inner.Reads);
        clock.Now = Start + KeptViews.Watched + TimeSpan.FromMinutes(1);
        await runs.Views.KeepWatchedAsync(CancellationToken.None);

        Assert.Equal(2, inner.Reads);
    }

    /// <summary>The workspace for the route: every read after the first waits on <see cref="Gate"/>, and each answers a new read.</summary>
    private sealed class GatedRuns : IProcessRunsReader
    {
        private int _reads;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads => Volatile.Read(ref _reads);

        public async Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
        {
            if (Interlocked.Increment(ref _reads) > 1)
            {
                await Gate.Task.WaitAsync(cancellation);
            }
            return new ProcessRunsRead(true, null, [], ProcessTally.None);
        }

        public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(ProcessFold.Unread("test"));

        public string? TraceHref(string operationId) => null;

        public string? ProcessHref(string process) => null;
    }
    // #endregion runs

    [Fact]
    public void One_keeper_keeps_the_Cloud_cards_the_plan_the_Azure_card_and_the_runs()
    {
        var kept = factory.Services.GetServices<IKeptViews>().Select(views => views.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["Azure card", "Cloud cards", "plan card", "run histories"], kept);
        Assert.Single(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ViewKeeper>());
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
