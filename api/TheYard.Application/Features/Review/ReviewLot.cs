// The review route's short path, stop 3 (ADR: The review route gives a reason before it asks, the addendum on the
// short path): one live lot a stranger can open, the live auction that closes soonest with five minutes still to run,
// whether or not anybody has bid, its bid, and how many vehicles the store holds. The room opens it: before the lot is
// read, the site's own simulated bidders (ADR: Competing bidders) take one turn on it through the room's own rules, as a
// command of its own, so the page never shows a lot that looks dead. A page strangers open cannot ask the catalogue and
// the room on every visit, so one answer a store is kept fifteen seconds and handed to every visitor in that window,
// one build at a time, the way the first screen's pulse is kept: at most one turn of the room in each.
using System.Collections.Concurrent;
using TheYard.Application.Shared;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Features.Review;

// #region review-lot
/// <summary>The lot the short path shows: the live auction closing soonest with five minutes to run, or none, and the store's size.</summary>
/// <param name="Live">True when a live auction was found.</param>
/// <param name="VehicleId">The vehicle's id, which ?vehicle= opens, or null when none is live.</param>
/// <param name="Name">Year, make, model and trim, or null.</param>
/// <param name="CurrentBid">The bid it stands at, in whole dollars, or null when nobody, the room included, has bid.</param>
/// <param name="OpeningBid">The opening ask, in whole dollars, printed when there is no bid; null when none is live.</param>
/// <param name="EndsAtMs">When its auction closes, milliseconds since the epoch, or null.</param>
/// <param name="Total">How many vehicles the store holds.</param>
public sealed record ReviewLot(bool Live, string? VehicleId, string? Name, int? CurrentBid, int? OpeningBid, long? EndsAtMs, int Total);

/// <summary>The short path's lot for the request's store.</summary>
public sealed record ReadReviewLotQuery;

/// <summary>The room's one turn on the short path's lot, before the lot is read: the id of the lot it opened, or null.</summary>
public sealed record OpenReviewLotCommand;

/// <summary>The rule that picks the lot, pure: of the live auctions, the one closing soonest with five minutes to run, never a sold one.</summary>
public static class ReviewLots
{
    /// <summary>The least time a lot has left: a stranger who opens it has time to watch the room bid.</summary>
    public static readonly TimeSpan Left = TimeSpan.FromMinutes(5);

    /// <summary>How many of the auctions closing soonest the pick looks at; the sold ones are the only ones it passes over.</summary>
    public const int Looked = 20;

    /// <summary>The id of the live auction closing soonest with <see cref="Left"/> to run, or null when there is none.</summary>
    /// <param name="candidates">Vehicle ids, live or not; only a live one with time left is picked.</param>
    /// <param name="sold">Whether a vehicle has been bought outright.</param>
    /// <param name="clock">The server's clock.</param>
    public static string? Soonest(IEnumerable<string> candidates, Func<string, bool> sold, AuctionClock clock) =>
        candidates
            .Distinct(StringComparer.Ordinal)
            .Where(id => !sold(id))
            .Select(id => (Id: id, Window: AuctionSchedule.Window(id, clock.AnchorMs)))
            .Where(lot => AuctionSchedule.Status(lot.Window, clock.NowMs) == AuctionStatus.Live
                && lot.Window.EndsAtMs - clock.NowMs >= (long)Left.TotalMilliseconds)
            .OrderBy(lot => lot.Window.EndsAtMs)
            .ThenBy(lot => lot.Id, StringComparer.Ordinal)
            .Select(lot => lot.Id)
            .FirstOrDefault();

    /// <summary>
    /// One refresh of the lot: the room's turn is sent to its own handler first, then the lot it opened is read, so the
    /// bid the tile prints is the room's real bid in the store and opening the lot shows the same number.
    /// </summary>
    /// <param name="open">The room's turn, the command's handler.</param>
    /// <param name="reader">The store's lot reader.</param>
    public static Func<CancellationToken, Task<ReviewLot>> OpenedBy(OpenReviewLotHandler open, ReviewLotReader reader) =>
        async cancellation => await reader.ReadAsync(await open.HandleAsync(new OpenReviewLotCommand(), cancellation), cancellation);
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

/// <summary>The short path's lot for one store, kept by the keeper and refreshed, the room's turn then the read, when it is gone.</summary>
/// <param name="keeper">The fifteen-second keeper every visitor shares.</param>
/// <param name="store">The request's store key.</param>
/// <param name="refresh">The store's refresh, <see cref="ReviewLots.OpenedBy"/>: the query itself holds no writer.</param>
public sealed class ReadReviewLotHandler(ReviewLotKeeper keeper, string store, Func<CancellationToken, Task<ReviewLot>> refresh) : IQueryHandler<ReadReviewLotQuery, ReviewLot>
{
    /// <summary>The lot, kept or refreshed.</summary>
    /// <param name="query">Nothing to ask.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<ReviewLot> HandleAsync(ReadReviewLotQuery query, CancellationToken cancellation) => keeper.ReadAsync(store, refresh, cancellation);
}

/// <summary>
/// The room opens the short path's lot: it picks the live auction closing soonest with five minutes to run and takes one
/// turn on it, through the same call the room's round makes (IRoomWriter.Tick with everybody's standing and the clock),
/// so the room's own rules hold: its ceiling at twice the opening ask, its grace on a buyer's lead, never a buy-out.
/// </summary>
/// <param name="standings">The store's vehicles as a query reads them, for the auctions closing soonest.</param>
/// <param name="inventory">The catalogue, for the vehicle at the dataset's figures, as the room's round hands it.</param>
/// <param name="bids">Everybody's bids, reading side.</param>
/// <param name="room">The room, writing side.</param>
/// <param name="clock">The server's auction clock.</param>
public sealed class OpenReviewLotHandler(StandingReader standings, IStockReader inventory, IBidReader bids, IRoomWriter room, Func<AuctionClock> clock) : ICommandHandler<OpenReviewLotCommand, string?>
{
    /// <summary>The id of the lot the room took its turn on, or null when no auction is live with time left.</summary>
    /// <param name="command">Nothing to ask.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string?> HandleAsync(OpenReviewLotCommand command, CancellationToken cancellation)
    {
        var now = clock();
        // Searched as the store stands five minutes from now: the live auctions then, closing soonest, are the ones with
        // at least that long to run, so the few closing in the next minutes never crowd the page out.
        var later = now with { NowMs = now.NowMs + (long)ReviewLots.Left.TotalMilliseconds };
        var page = await standings.SearchAsync(new VehicleFilter { Status = AuctionStatus.Live }, later, VehicleSort.EndingSoonest, ReviewLots.Looked, 0, cancellation);
        var sold = page.Vehicles.Where(found => found.Sold).Select(found => found.Vehicle.Id).ToHashSet(StringComparer.Ordinal);
        string? id = ReviewLots.Soonest(page.Vehicles.Select(found => found.Vehicle.Id), sold.Contains, now);
        if (id is null)
        {
            return null;
        }
        if ((await inventory.FindManyAsync([id], cancellation)).FirstOrDefault() is { } vehicle)
        {
            room.Tick([vehicle], bids.StandingAsBids(), now, maxBids: 1);
        }
        return id;
    }
}

/// <summary>Reads one store's lot after the room's turn: the work the keeper saves fifteen seconds of.</summary>
/// <param name="standings">The store's vehicles as a query reads them.</param>
/// <param name="clock">The server's auction clock.</param>
public sealed class ReviewLotReader(StandingReader standings, Func<AuctionClock> clock)
{
    /// <summary>The lot the room opened, at the price it stands at, or none, and the store's count.</summary>
    /// <param name="id">The lot the room opened, or null when none is live.</param>
    /// <param name="cancellation">The read's cancellation.</param>
    public async Task<ReviewLot> ReadAsync(string? id, CancellationToken cancellation)
    {
        var now = clock();
        int total = (await standings.SearchAsync(new VehicleFilter(), now, VehicleSort.EndingSoonest, 1, 0, cancellation)).Total;
        if (id is null || await standings.FindAsync(id, cancellation) is not { } found)
        {
            return new ReviewLot(false, null, null, null, null, null, total);
        }
        var vehicle = found.Vehicle;
        return new ReviewLot(true, vehicle.Id, $"{vehicle.Year} {vehicle.Make} {vehicle.Model} {vehicle.Trim}".Trim(), vehicle.CurrentBid, vehicle.StartingBid, found.WindowAt(now).EndsAtMs, total);
    }
}
// #endregion review-lot
