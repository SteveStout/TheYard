using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Review;
using TheYard.Application.Features.Review;

namespace TheYard.Tests;

/// <summary>
/// The review route's command and query without a host (ADR: Ten minutes, seven stops, the addendum on the
/// reach rows), over hand-written fakes and the memory keeper: what a report keeps, what the card counts,
/// the four windows, and the collector folding a minute's reports into one row a visitor before it writes.
/// </summary>
public sealed class ReviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock that always reads the one instant.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The collector's port as a list, so a test can read what the command handed on.</summary>
    private sealed class Offers : IReviewReachOffers
    {
        public List<ReviewReach> Taken { get; } = [];

        public void Offer(ReviewReach reach) => Taken.Add(reach);
    }

    [Fact]
    public async Task A_report_is_refused_outside_the_route_kept_for_a_person_and_answered_for_the_site_itself()
    {
        var offers = new Offers();
        var handler = new RecordReviewReachHandler(offers, new FixedClock(Now));

        Assert.False(await handler.HandleAsync(new RecordReviewReachCommand("hash", 8, "zzrev1"), CancellationToken.None));
        Assert.False(await handler.HandleAsync(new RecordReviewReachCommand("hash", -1, null), CancellationToken.None));
        Assert.True(await handler.HandleAsync(new RecordReviewReachCommand("hash", 3, "zzrev1"), CancellationToken.None));
        // A code that is not a code is dropped, not refused.
        Assert.True(await handler.HandleAsync(new RecordReviewReachCommand("hash", 4, "Steve Stout"), CancellationToken.None));
        // The site's own read, a scanner or the operator carries no hash: answered, never kept.
        Assert.True(await handler.HandleAsync(new RecordReviewReachCommand(null, 2, "zzrev1"), CancellationToken.None));

        Assert.Equal(
            [new ReviewReach("hash", "2026-11-04", 3, "zzrev1", Now), new ReviewReach("hash", "2026-11-04", 4, null, Now)],
            offers.Taken);
    }

    [Fact]
    public async Task The_funnel_counts_each_visitor_once_at_the_highest_stop_with_eight_places_and_the_codes_newest_first()
    {
        var store = new MemoryReviewStore();
        await store.KeepAsync([
            new("a", "2026-11-03", 2, "zzrev1", Now.AddDays(-1).AddHours(-2), Now.AddDays(-1).AddHours(-2).AddMinutes(3)),
            new("a", "2026-11-04", 5, null, Now.AddHours(-2), Now.AddHours(-1)),
            new("b", "2026-11-04", 0, null, Now.AddHours(-3), Now.AddHours(-3)),
            new("c", "2026-11-04", 7, "zzrev2", Now.AddMinutes(-40), Now.AddMinutes(-29)),
            new("d", "2026-10-01", 7, "zzold1", Now.AddDays(-34), Now.AddDays(-34)),
        ], CancellationToken.None);
        var handler = new ReadReviewFunnelHandler(store, new FixedClock(Now));

        var week = await handler.HandleAsync(new ReadReviewFunnelQuery(ReviewWindow.Week), CancellationToken.None);
        Assert.True(week.Available);
        Assert.Equal("7d", week.Window);
        Assert.Equal(3, week.Opened);
        Assert.Equal([3, 2, 2, 2, 2, 2, 1, 1], week.ReachedByStop);
        Assert.Equal([new ReviewCodeRow("zzrev2", 7, 11, "2026-11-04"), new ReviewCodeRow("zzrev1", 2, 3, "2026-11-03")], week.ByCode);
        Assert.Equal("2026-10-01", week.CountedFrom);
        Assert.Equal(store.Source, week.Source);

        var quarter = await handler.HandleAsync(new ReadReviewFunnelQuery(ReviewWindow.Quarter), CancellationToken.None);
        Assert.Equal(4, quarter.Opened);
        Assert.Equal(ReadReviewFunnelHandler.Places, quarter.ReachedByStop.Count);

        // The day window reads back 24 hours, not to the start of yesterday.
        var day = await handler.HandleAsync(new ReadReviewFunnelQuery(ReviewWindow.Day), CancellationToken.None);
        Assert.Equal(3, day.Opened);
        Assert.DoesNotContain(day.ByCode, row => row.Code == "zzrev1");
    }

    [Fact]
    public async Task A_keeper_that_keeps_nothing_says_why_and_the_funnel_is_empty_not_missing()
    {
        var handler = new ReadReviewFunnelHandler(new NoReviewStore("no document store here"), new FixedClock(Now));
        var funnel = await handler.HandleAsync(new ReadReviewFunnelQuery(ReviewWindow.Month), CancellationToken.None);
        Assert.False(funnel.Available);
        Assert.Equal(("30d", 0, "no document store here"), (funnel.Window, funnel.Opened, funnel.Source));
        Assert.Equal(Enumerable.Repeat(0, 8), funnel.ReachedByStop);
        Assert.Empty(funnel.ByCode);
        Assert.Null(funnel.CountedFrom);
    }

    [Theory]
    [InlineData("24h", ReviewWindow.Day, 24)]
    [InlineData("7d", ReviewWindow.Week, 168)]
    [InlineData("30d", ReviewWindow.Month, 720)]
    [InlineData("90d", ReviewWindow.Quarter, 2160)]
    [InlineData(null, ReviewWindow.Week, 168)]
    [InlineData("1y", ReviewWindow.Week, 168)]
    public void Four_windows_are_spelled_the_way_the_links_card_spells_its_own(string? name, ReviewWindow window, int hours)
    {
        Assert.Equal(window, ReviewWindows.Parse(name));
        Assert.Equal(TimeSpan.FromHours(hours), ReviewWindows.LengthOf(window));
        Assert.Equal(["24h", "7d", "30d", "90d"], ReviewWindows.ByName.Keys);
        Assert.Equal(name is "24h" or "7d" or "30d" or "90d" ? name : "7d", ReviewWindows.NameOf(window));
    }

    [Fact]
    public async Task The_collector_folds_a_minute_of_reports_into_one_row_a_visitor_before_it_writes()
    {
        var store = new MemoryReviewStore();
        var collector = new ReviewReachCollector(store, NullLogger<ReviewReachCollector>.Instance);
        foreach (int stop in new[] { 0, 1, 2, 3, 1 })
        {
            collector.Offer(new ReviewReach("a", "2026-11-04", stop, stop == 0 ? "zzrev1" : null, Now.AddMinutes(stop)));
        }
        await collector.DrainAsync(CancellationToken.None);
        collector.Offer(new ReviewReach("a", "2026-11-04", 2, null, Now.AddMinutes(9)));
        await collector.DrainAsync(CancellationToken.None);

        var row = Assert.Single(await store.RowsSinceAsync("2026-11-04", CancellationToken.None));
        Assert.Equal((3, "zzrev1", Now, Now.AddMinutes(9)), (row.Stop, row.Code, row.First, row.Last));
        Assert.Equal("2026-11-04", await store.FirstDayAsync(CancellationToken.None));
    }
}
