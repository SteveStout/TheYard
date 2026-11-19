using TheYard.Api.Features.Docs;
using TheYard.Application.Features.Review;

namespace TheYard.Tests;

/// <summary>
/// The review route's short path (ADR: The review route gives a reason before it asks, the addendum on the short
/// path): the lot is the live auction closing soonest among those bid on and never a sold one, one answer a store is
/// kept fifteen seconds and built once however many ask, and the chart's readings are the Quality page's own fence,
/// all without a host. No store is touched: the pick is pure, the keeper takes a hand-written read, the lot is read from
/// one store's parts in memory and the history is a throwaway copy (RULE 79). The two routes are ReviewShortPathRouteTests'.
/// </summary>
public sealed class ReviewShortPathTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock a test moves by hand.</summary>
    private sealed class HandClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void The_lot_is_the_live_auction_closing_soonest_among_those_bid_on_and_never_a_sold_one()
    {
        var clock = AuctionClock.Utc(Noon);
        // Probe ids until there are live ones and one that is not live, from the schedule's own rule.
        var probes = Enumerable.Range(0, 400).Select(i => $"probe-{i}").ToList();
        var live = probes.Where(id => AuctionSchedule.Status(AuctionSchedule.Window(id, clock.AnchorMs), clock.NowMs) == AuctionStatus.Live).ToList();
        var notLive = probes.First(id => !live.Contains(id));
        Assert.True(live.Count >= 2, "the probes hold at least two live auctions");
        var soonest = live.OrderBy(id => AuctionSchedule.Window(id, clock.AnchorMs).EndsAtMs).ThenBy(id => id, StringComparer.Ordinal).ToList();

        Assert.Equal(soonest[0], ReviewLots.Soonest([.. live, notLive, live[0]], _ => false, clock));
        Assert.Equal(soonest[1], ReviewLots.Soonest(live, id => id == soonest[0], clock));
        Assert.Null(ReviewLots.Soonest([notLive], _ => false, clock));
        Assert.Null(ReviewLots.Soonest([], _ => false, clock));
    }

    [Fact]
    public void Two_lots_closing_at_the_same_moment_are_told_apart_by_id_so_every_visitor_sees_the_same_one()
    {
        var clock = AuctionClock.Utc(Noon);
        var byEnd = Enumerable.Range(0, 3000)
            .Select(i => $"tie-{i}")
            .Where(id => AuctionSchedule.Status(AuctionSchedule.Window(id, clock.AnchorMs), clock.NowMs) == AuctionStatus.Live)
            .GroupBy(id => AuctionSchedule.Window(id, clock.AnchorMs).EndsAtMs)
            .FirstOrDefault(group => group.Count() > 1);
        Assert.NotNull(byEnd);
        var tied = byEnd.OrderBy(id => id, StringComparer.Ordinal).ToList();
        // Whatever order the bids arrive in, the first by id is the lot, so two visitors never see two lots.
        Assert.Equal(tied[0], ReviewLots.Soonest([.. tied.AsEnumerable().Reverse()], _ => false, clock));
        Assert.Equal(tied[0], ReviewLots.Soonest(tied, _ => false, clock));
    }

    [Fact]
    public async Task One_lot_a_store_is_kept_fifteen_seconds_and_built_once_however_many_ask()
    {
        var clock = new HandClock(Noon);
        var keeper = new ReviewLotKeeper(clock);
        int reads = 0;
        async Task<ReviewLot> Read(CancellationToken cancellation)
        {
            Interlocked.Increment(ref reads);
            await Task.Delay(20, cancellation);
            return new ReviewLot(true, "v1", "2019 Make Model Trim", 12_000, 1, 100_000);
        }

        var many = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => keeper.ReadAsync("sql", Read, CancellationToken.None)));
        Assert.Equal(1, reads);
        Assert.All(many, lot => Assert.Equal("v1", lot.VehicleId));

        clock.Now = Noon.AddSeconds(14);
        await keeper.ReadAsync("sql", Read, CancellationToken.None);
        Assert.Equal(1, reads);
        // The other store keeps its own lot.
        await keeper.ReadAsync("cosmos", Read, CancellationToken.None);
        Assert.Equal(2, reads);
        clock.Now = Noon.AddSeconds(15);
        await keeper.ReadAsync("sql", Read, CancellationToken.None);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task The_handler_reads_the_live_lot_with_a_bid_from_the_store_s_own_parts_and_none_before_anyone_bids()
    {
        var clock = TestAuctions.AtNoon();
        var bidOn = TestAuctions.Live("lot", currentBid: 20_000);
        var quiet = TestAuctions.Live("quiet", currentBid: 30_000);
        var auction = TestAuctions.Over(bidOn, quiet);
        var reader = new ReviewLotReader(auction.Standings, auction.Bids, auction.Room, () => clock);
        var handler = new ReadReviewLotHandler(new ReviewLotKeeper(new HandClock(Noon)), "sql", reader);

        var before = await reader.ReadAsync(CancellationToken.None);
        Assert.False(before.Live);
        Assert.Equal(2, before.Total);

        var placed = await auction.PlaceBidAsync((await auction.FindAsync(bidOn.Id))!, 21_000, clock, "buyer");
        Assert.Equal(BidOutcomeKind.Accepted, placed.Kind);
        var lot = await handler.HandleAsync(new ReadReviewLotQuery(), CancellationToken.None);
        Assert.True(lot.Live);
        Assert.Equal((bidOn.Id, 21_000, 2), (lot.VehicleId, lot.CurrentBid, lot.Total));
        Assert.Equal(AuctionSchedule.Window(bidOn.Id, clock.AnchorMs).EndsAtMs, lot.EndsAtMs);
        Assert.Contains(bidOn.Make, lot.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lot_bought_outright_is_never_the_one_shown_and_the_next_live_one_with_a_bid_is()
    {
        var clock = TestAuctions.AtNoon();
        var bought = TestAuctions.Live("bought", currentBid: 20_000, buyNow: 40_000);
        var other = TestAuctions.Live("other", currentBid: 25_000);
        var auction = TestAuctions.Over(bought, other);
        var reader = new ReviewLotReader(auction.Standings, auction.Bids, auction.Room, () => clock);

        Assert.Equal(BidOutcomeKind.Accepted, (await auction.PlaceBidAsync((await auction.FindAsync(other.Id))!, 26_000, clock, "buyer")).Kind);
        var sale = await auction.BuyNowAsync((await auction.FindAsync(bought.Id))!, clock, "buyer");
        Assert.Equal(BidOutcomeKind.Won, sale.Kind);

        var lot = await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(other.Id, lot.VehicleId);
        Assert.Equal(26_000, lot.CurrentBid);
    }

    [Fact]
    public async Task A_caller_that_stops_waiting_is_let_go_and_the_lot_is_still_kept_for_the_next()
    {
        var keeper = new ReviewLotKeeper(new HandClock(Noon));
        var release = new TaskCompletionSource();
        int reads = 0;
        async Task<ReviewLot> Slow(CancellationToken cancellation)
        {
            Interlocked.Increment(ref reads);
            await release.Task;
            return new ReviewLot(false, null, null, null, null, 7);
        }

        var first = keeper.ReadAsync("sql", Slow, CancellationToken.None);
        using var impatient = new CancellationTokenSource();
        var second = keeper.ReadAsync("sql", Slow, impatient.Token);
        await impatient.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.SetResult();
        Assert.Equal(7, (await first).Total);
        Assert.Equal(7, (await keeper.ReadAsync("sql", Slow, CancellationToken.None)).Total);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task The_chart_reads_the_Quality_page_s_own_fence_and_a_number_the_history_lacks_reads_as_none()
    {
        IQualityTrend trend = new QualityTrendFence(Repo.Root());
        var readings = await trend.ReadingsAsync(ReadReviewTrendHandler.Number, CancellationToken.None);
        Assert.NotEmpty(readings);
        Assert.All(readings, line => Assert.Matches(@"^\d{4}-\d{2}-\d{2} \| [^|]* \| \d+(\.\d+)? \| \S+$", line));
        Assert.Equal(
            QualityCounts.Measure("quality-trend", ReadReviewTrendHandler.Number, Repo.Root()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            readings);
        Assert.Same(readings, await trend.ReadingsAsync(ReadReviewTrendHandler.Number, CancellationToken.None));
        Assert.Empty(await trend.ReadingsAsync("no_such_number", CancellationToken.None));

        var answer = await new ReadReviewTrendHandler(trend).HandleAsync(new ReadReviewTrendQuery(), CancellationToken.None);
        Assert.Equal((ReadReviewTrendHandler.Number, readings), (answer.Number, answer.Readings));
    }

    [Fact]
    public async Task The_readings_are_kept_until_the_history_file_changes_and_read_again_after()
    {
        // A throwaway copy of the history in a folder born and removed with the test (RULE 79): the build's own file
        // is never written.
        string root = Path.Combine(Path.GetTempPath(), "review-trend-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        try
        {
            string history = Path.Combine(data, "quality-history.json");
            File.Copy(Path.Combine(Repo.Root(), "data", "quality-history.json"), history);
            var trend = new QualityTrendFence(root);
            var first = await trend.ReadingsAsync(ReadReviewTrendHandler.Number, CancellationToken.None);
            Assert.Same(first, await trend.ReadingsAsync(ReadReviewTrendHandler.Number, CancellationToken.None));

            // The promote writes a new row: the next read is a new answer, read from the new file.
            File.WriteAllText(history, """{ "rows": [ { "date": "2026-11-19", "version": "9.9.9.9", "source": "quality.py", "numbers": { "async_without_cancellation": 0 } } ] }""");
            File.SetLastWriteTimeUtc(history, File.GetLastWriteTimeUtc(history).AddMinutes(1));
            var after = await trend.ReadingsAsync(ReadReviewTrendHandler.Number, CancellationToken.None);
            Assert.NotSame(first, after);
            Assert.Equal(["2026-11-19 | 9.9.9.9 | 0 | quality.py"], after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
