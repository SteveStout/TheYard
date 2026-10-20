// The caller's bids as the page reads them: their standing on every vehicle, their history, one
// round of the room with their badges after it, and their start-over.
using TheYard.Domain;

namespace TheYard.Application.Features.Bids;

/// <summary>The caller's standing on every vehicle they have bid on; a visitor with no session has none.</summary>
/// <param name="auction">The request's auction.</param>
public sealed class MyBidsHandler(Auction auction)
{
    /// <summary>The caller's badges, keyed by vehicle id, or an empty map for nobody.</summary>
    /// <param name="userId">The signed-in bidder, or null.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<IReadOnlyDictionary<string, BidView>> HandleAsync(string? userId, CancellationToken cancellation) =>
        Task.FromResult(userId is null ? new Dictionary<string, BidView>(StringComparer.Ordinal) : auction.BidsOf(userId));
}

/// <summary>The caller's bids, newest first, with the vehicle each is on.</summary>
/// <param name="auction">The request's auction.</param>
public sealed class BidHistoryHandler(Auction auction)
{
    /// <summary>The signed-in bidder's history.</summary>
    /// <param name="userId">The signed-in bidder.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<BidHistory> HandleAsync(string userId, CancellationToken cancellation) => auction.HistoryOfAsync(userId);
}

/// <summary>One round of bidding by the room on the server's clock, and the caller's badges after it.</summary>
/// <param name="auction">The request's auction.</param>
/// <param name="clock">The server's clock.</param>
public sealed class RoomRoundHandler(Auction auction, Func<AuctionClock> clock)
{
    /// <summary>Runs one round and answers how many auctions it raised and the caller's standing afterwards.</summary>
    /// <param name="userId">The signed-in bidder, or null.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<RoomRound> HandleAsync(string? userId, CancellationToken cancellation)
    {
        var raised = await auction.RoomRoundAsync(clock());
        return new RoomRound(raised.Count, userId is null ? new Dictionary<string, BidView>(StringComparer.Ordinal) : auction.BidsOf(userId));
    }
}

/// <summary>One person's start-over: their bids forgotten, and the room's answers to them; nobody else's move.</summary>
/// <param name="auction">The request's auction.</param>
public sealed class ResetBidsHandler(Auction auction)
{
    /// <summary>Forgets the caller's bids; false when there is nobody signed in to forget.</summary>
    /// <param name="userId">The signed-in bidder, or null.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<bool> HandleAsync(string? userId, CancellationToken cancellation)
    {
        if (userId is null)
        {
            return false;
        }
        await auction.ResetAsync(userId);
        return true;
    }
}

/// <summary>One round of the room.</summary>
/// <param name="Raised">How many auctions the room raised.</param>
/// <param name="Bids">The caller's standing afterwards, keyed by vehicle id.</param>
public sealed record RoomRound(int Raised, IReadOnlyDictionary<string, BidView> Bids);
