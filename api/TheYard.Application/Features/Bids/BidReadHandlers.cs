// The caller's bids as the page reads them: their standing on every vehicle, their history, one
// round of the room with their badges after it, and their start-over. Each is a query or a command
// with one handler (ADR-115: A request is a query or a command).
using TheYard.Application.Shared;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Features.Bids;

/// <summary>The caller's standing on every vehicle they have bid on; a visitor with no session has none.</summary>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
public sealed class ReadMyBidsHandler(StandingReader standings) : IQueryHandler<ReadMyBidsQuery, IReadOnlyDictionary<string, BidView>>
{
    /// <summary>The caller's badges, keyed by vehicle id, or an empty map for nobody.</summary>
    /// <param name="query">Who is asking.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<IReadOnlyDictionary<string, BidView>> HandleAsync(ReadMyBidsQuery query, CancellationToken cancellation) =>
        Task.FromResult(query.UserId is null ? new Dictionary<string, BidView>(StringComparer.Ordinal) : standings.BidsOf(query.UserId));
}

/// <summary>The caller's standing on every vehicle they have bid on.</summary>
/// <param name="UserId">The signed-in bidder, or null.</param>
public sealed record ReadMyBidsQuery(string? UserId);

/// <summary>The caller's bids, newest first, with the vehicle each is on.</summary>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
public sealed class ReadBidHistoryHandler(StandingReader standings) : IQueryHandler<ReadBidHistoryQuery, BidHistory>
{
    /// <summary>The signed-in bidder's history.</summary>
    /// <param name="query">Whose history.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<BidHistory> HandleAsync(ReadBidHistoryQuery query, CancellationToken cancellation) => standings.HistoryOfAsync(query.UserId, cancellation);
}

/// <summary>A signed-in bidder's history.</summary>
/// <param name="UserId">The signed-in bidder.</param>
public sealed record ReadBidHistoryQuery(string UserId);

/// <summary>One round of bidding by the room on the server's clock, and the caller's badges after it.</summary>
/// <param name="bids">Everybody's bids, reading side: the standing the room answers.</param>
/// <param name="inventory">The catalogue, reading side: the contested vehicles and the first live ones.</param>
/// <param name="room">The room, writing side: the round itself.</param>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
/// <param name="clock">The server's clock.</param>
public sealed class RoomRoundHandler(IBidReader bids, IStockReader inventory, IRoomWriter room, StandingReader standings, Func<AuctionClock> clock) : ICommandHandler<RoomRoundCommand, RoomRound>
{
    /// <summary>Runs one round and answers how many auctions it raised and the caller's standing afterwards.</summary>
    /// <param name="command">Who is watching the round.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<RoomRound> HandleAsync(RoomRoundCommand command, CancellationToken cancellation)
    {
        var raised = await RoundAsync(bids, inventory, room, clock(), cancellation);
        return new RoomRound(raised.Count, command.UserId is null ? new Dictionary<string, BidView>(StringComparer.Ordinal) : standings.BidsOf(command.UserId));
    }

    /// <summary>How many live auctions the room looks at in one round, beside the ones somebody is bidding on.</summary>
    private const int RoomRoundLiveCount = 40;

    // #region room-round
    /// <summary>
    /// One round of bidding by the room. The room answers a price rather than a person, so it is
    /// handed everybody's standing, not one account's. Its candidates are every vehicle somebody
    /// is bidding on plus the first live auctions in the catalogue, so the grid moves even for a
    /// visitor who has bid on nothing. The first ones rather than a search, because a search
    /// counts every match to keep a handful of them; the store answers the first live ones off its
    /// own order and stops at the fortieth.
    /// </summary>
    /// <param name="bids">Everybody's bids, reading side.</param>
    /// <param name="inventory">The catalogue, reading side.</param>
    /// <param name="room">The room, writing side.</param>
    /// <param name="clock">The server's clock.</param>
    /// <param name="cancellation">The request's: the two catalogue reads are the whole cost of a round, and the round itself is memory.</param>
    /// <returns>The ids the room raised.</returns>
    public static async Task<IReadOnlyList<string>> RoundAsync(IBidReader bids, IStockReader inventory, IRoomWriter room, AuctionClock clock, CancellationToken cancellation)
    {
        var standing = bids.StandingAsBids();
        var found = (await inventory.FindManyAsync(standing.Keys.ToList(), cancellation)).ToDictionary(v => v.Id, StringComparer.Ordinal);
        var contested = standing.Keys.Where(found.ContainsKey).Select(id => found[id]);
        var live = await inventory.FirstLiveAsync(clock, RoomRoundLiveCount, cancellation);
        var candidates = contested.Concat(live).DistinctBy(v => v.Id).ToList();
        return room.Tick(candidates, standing, clock);
    }
    // #endregion room-round
}

/// <summary>One round of the room, which bids, so it is a command.</summary>
/// <param name="UserId">The signed-in bidder whose badges come back, or null.</param>
public sealed record RoomRoundCommand(string? UserId);

/// <summary>One person's start-over: their bids forgotten, and the room's answers to them; nobody else's move.</summary>
/// <param name="bids">Everybody's bids, writing side: the buyer's start-over.</param>
/// <param name="room">The room, writing side: its answers on the vehicles the reset left alone.</param>
public sealed class ResetBidsHandler(IBidWriter bids, IRoomWriter room) : ICommandHandler<ResetBidsCommand, bool>
{
    /// <summary>Forgets the caller's bids; false when there is nobody signed in to forget.</summary>
    /// <param name="command">Whose bids.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<bool> HandleAsync(ResetBidsCommand command, CancellationToken cancellation)
    {
        if (command.UserId is null)
        {
            return false;
        }
        // #region reset
        // One buyer's start-over: their bids go, and so do the room's answers on the vehicles nobody
        // else is bidding on any more. Leaving the room's counter-bid standing would make the reset
        // clear one side of an auction and not the other.
        room.Forget(await bids.ResetAsync(command.UserId, cancellation));
        // #endregion reset
        return true;
    }
}

/// <summary>One person's start-over.</summary>
/// <param name="UserId">The signed-in bidder, or null.</param>
public sealed record ResetBidsCommand(string? UserId);

/// <summary>One round of the room.</summary>
/// <param name="Raised">How many auctions the room raised.</param>
/// <param name="Bids">The caller's standing afterwards, keyed by vehicle id.</param>
public sealed record RoomRound(int Raised, IReadOnlyDictionary<string, BidView> Bids);
