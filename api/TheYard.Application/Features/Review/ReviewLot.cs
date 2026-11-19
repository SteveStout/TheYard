// The review route's short path, stop 3 (ADR: The review route gives a reason before it asks, the addendum on the
// short path): one live lot a stranger can open, the auction that closes soonest among those somebody has bid on, its
// standing bid, and how many vehicles the store holds. A page strangers open cannot ask the catalogue and the bids on
// every visit, so one answer a store is kept fifteen seconds and handed to every visitor in that window, one build at
// a time, the way the first screen's pulse is kept.
using System.Collections.Concurrent;
using TheYard.Application.Shared;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Features.Review;

// #region review-lot
/// <summary>The lot the short path shows: the live auction closing soonest with a bid on it, or none, and the store's size.</summary>
/// <param name="Live">True when an auction with a bid is live right now.</param>
/// <param name="VehicleId">The vehicle's id, which ?vehicle= opens, or null when none is live.</param>
/// <param name="Name">Year, make, model and trim, or null.</param>
/// <param name="CurrentBid">The bid it stands at, in whole dollars, or null.</param>
/// <param name="EndsAtMs">When its auction closes, milliseconds since the epoch, or null.</param>
/// <param name="Total">How many vehicles the store holds.</param>
public sealed record ReviewLot(bool Live, string? VehicleId, string? Name, int? CurrentBid, long? EndsAtMs, int Total);

/// <summary>The short path's lot for the request's store.</summary>
public sealed record ReadReviewLotQuery;

/// <summary>The rule that picks the lot, pure: of the vehicles somebody has bid on, the live one closing soonest, never a sold one.</summary>
public static class ReviewLots
{
    /// <summary>The id of the live auction closing soonest among those bid on, or null when none is live.</summary>
    /// <param name="bidOn">The ids somebody, a buyer or the room, has a bid on.</param>
    /// <param name="sold">Whether a vehicle has been bought outright.</param>
    /// <param name="clock">The server's clock.</param>
    public static string? Soonest(IEnumerable<string> bidOn, Func<string, bool> sold, AuctionClock clock) =>
        bidOn
            .Distinct(StringComparer.Ordinal)
            .Where(id => !sold(id))
            .Select(id => (Id: id, Window: AuctionSchedule.Window(id, clock.AnchorMs)))
            .Where(lot => AuctionSchedule.Status(lot.Window, clock.NowMs) == AuctionStatus.Live)
            .OrderBy(lot => lot.Window.EndsAtMs)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => lot.Id)
            .FirstOrDefault();
}

/// <summary>Keeps one lot a store for <see cref="Every"/>, built by one caller at a time while the others wait for it.</summary>
/// <param name="clock">The server's clock, for the age of a kept answer.</param>
public sealed class ReviewLotKeeper(TimeProvider clock)
{
    /// <summary>How long a lot is handed out before it is read again.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, ReviewLot Lot)> _kept = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The store's lot, read with <paramref name="read"/> at most once every <see cref="Every"/>.</summary>
    /// <param name="store">The store's key: each site keeps its own lot.</param>
    /// <param name="read">How to read the lot when the kept one is gone.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ReviewLot> ReadAsync(string store, Func<CancellationToken, Task<ReviewLot>> read, CancellationToken cancellation)
    {
        if (Fresh(store) is { } kept)
        {
            return kept;
        }
        await _gate.WaitAsync(cancellation);
        try
        {
            if (Fresh(store) is { } again)
            {
                return again;
            }
            var lot = await read(cancellation);
            _kept[store] = (clock.GetUtcNow(), lot);
            return lot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ReviewLot? Fresh(string store) =>
        _kept.TryGetValue(store, out var kept) && clock.GetUtcNow() - kept.At < Every ? kept.Lot : null;
}

/// <summary>The short path's lot for one store, kept by the keeper and read by the store's lot reader when it is gone.</summary>
/// <param name="keeper">The fifteen-second keeper every visitor shares.</param>
/// <param name="store">The request's store key.</param>
/// <param name="reader">The store's lot reader.</param>
public sealed class ReadReviewLotHandler(ReviewLotKeeper keeper, string store, ReviewLotReader reader) : IQueryHandler<ReadReviewLotQuery, ReviewLot>
{
    /// <summary>The lot, kept or read.</summary>
    /// <param name="query">Nothing to ask.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<ReviewLot> HandleAsync(ReadReviewLotQuery query, CancellationToken cancellation) => keeper.ReadAsync(store, reader.ReadAsync, cancellation);
}

/// <summary>Reads one store's lot from its bids, its room and its catalogue: the work the keeper saves fifteen seconds of.</summary>
/// <param name="standings">The store's vehicles as a query reads them.</param>
/// <param name="bids">Everybody's bids, reading side.</param>
/// <param name="room">The room's bids, reading side.</param>
/// <param name="clock">The server's auction clock.</param>
public sealed class ReviewLotReader(StandingReader standings, IBidReader bids, IRoomReader room, Func<AuctionClock> clock)
{
    /// <summary>The live auction closing soonest with a bid on it, or none, and the store's count.</summary>
    /// <param name="cancellation">The read's cancellation.</param>
    public async Task<ReviewLot> ReadAsync(CancellationToken cancellation)
    {
        var now = clock();
        int total = (await standings.SearchAsync(new VehicleFilter(), now, VehicleSort.EndingSoonest, 1, 0, cancellation)).Total;
        string? id = ReviewLots.Soonest(bids.Standing().Keys.Concat(room.Snapshot().Keys), bids.IsSold, now);
        if (id is null || await standings.FindAsync(id, cancellation) is not { } found)
        {
            return new ReviewLot(false, null, null, null, null, total);
        }
        var vehicle = found.Vehicle;
        return new ReviewLot(true, vehicle.Id, $"{vehicle.Year} {vehicle.Make} {vehicle.Model} {vehicle.Trim}".Trim(), vehicle.CurrentBid, found.WindowAt(now).EndsAtMs, total);
    }
}
// #endregion review-lot
