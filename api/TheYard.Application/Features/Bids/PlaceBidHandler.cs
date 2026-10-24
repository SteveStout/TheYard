// A bid or a Buy Now, as one use case: is the session on this store, does the vehicle exist, does
// the domain accept the action on the server's clock, and what the caller sees afterwards.
using TheYard.Application.Shared;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Features.Bids;

// #region place-bid
/// <summary>
/// Places a bid, or buys now, for one signed-in bidder (ADR: The onion cut into vertical slices,
/// rule 7). The order of the three questions is the contract the browser relies on: the session's
/// store first, then the vehicle, then the rules. The vehicle is read as it stands and the bid is
/// written through the bids' writing side, each handed in by the slice's Module; the clock is the server's.
/// </summary>
/// <param name="standings">The request store's vehicles as they stand, and the caller's badges: the reading side only.</param>
/// <param name="bids">Everybody's bids, writing side: the bid and the buy-now purchase.</param>
/// <param name="clock">The server's clock, read once per request.</param>
public sealed class PlaceBidHandler(StandingReader standings, IBidWriter bids, Func<AuctionClock> clock) : ICommandHandler<PlaceBidCommand, BidAnswer>
{
    /// <summary>Answers one bid or Buy Now.</summary>
    /// <param name="request">Who, on which vehicle, and the amount or none for Buy Now.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<BidAnswer> HandleAsync(PlaceBidCommand request, CancellationToken cancellation)
    {
        // A session bids where its account is. The token names the store that opened it, so
        // this check costs no lookup; a bid on the other store would be a row under an id that
        // store has no account for.
        if (!request.SessionOnThisStore)
        {
            return BidAnswer.Refused(BidAnswerKind.SessionElsewhere, null);
        }
        var now = clock();
        cancellation.ThrowIfCancellationRequested();
        if (await standings.FindAsync(request.VehicleId) is not { } vehicle)
        {
            return BidAnswer.Refused(BidAnswerKind.NoSuchVehicle, null);
        }
        // The bid is measured against the vehicle as it stands, the room's price included: measuring
        // against the dataset's figure alone would let a buyer retake the lead below the going rate.
        var outcome = request.Amount is { } amount
            ? await bids.PlaceBidAsync(vehicle.Vehicle, amount, now, request.UserId)
            : await bids.BuyNowAsync(vehicle.Vehicle, now, request.UserId);
        if (outcome.Kind == BidOutcomeKind.Rejected)
        {
            return BidAnswer.Refused(BidAnswerKind.Rejected, outcome.Reason);
        }
        // The room's answer rides back with the bid, so the badge is right the moment the
        // response lands. TryGetValue, because a reset from the same account in a second tab can
        // land between the bid and this read. The vehicle is read again after the bid, so the
        // price and the sold flag are the ones it left.
        return new BidAnswer(
            BidAnswerKind.Placed,
            outcome,
            null,
            standings.BidsOf(request.UserId).TryGetValue(request.VehicleId, out var standing) ? standing : null,
            await standings.FindAsync(request.VehicleId) ?? vehicle,
            now);
    }
}
// #endregion place-bid

/// <summary>One bid or Buy Now.</summary>
/// <param name="VehicleId">The vehicle.</param>
/// <param name="UserId">The signed-in bidder.</param>
/// <param name="SessionOnThisStore">True when the session was opened on the store this request is on.</param>
/// <param name="Amount">Whole dollars for a bid; null for Buy Now.</param>
public sealed record PlaceBidCommand(string VehicleId, string UserId, bool SessionOnThisStore, int? Amount);

/// <summary>What a bid came to.</summary>
public enum BidAnswerKind
{
    /// <summary>The bid or the purchase was taken.</summary>
    Placed,

    /// <summary>The session was opened on the other store.</summary>
    SessionElsewhere,

    /// <summary>No vehicle has the id.</summary>
    NoSuchVehicle,

    /// <summary>The rules refused it, with a reason.</summary>
    Rejected,
}

/// <summary>A bid's answer: what it came to, and for a bid that was taken, everything the badge and the card need.</summary>
/// <param name="Kind">What it came to.</param>
/// <param name="Outcome">The domain's outcome, for a bid that was taken.</param>
/// <param name="Reason">The rules' sentence, for a refused bid.</param>
/// <param name="Standing">The caller's standing on the vehicle afterwards, when it is still there.</param>
/// <param name="Vehicle">The vehicle as the bid left it.</param>
/// <param name="Clock">The server's clock the bid was decided on.</param>
public sealed record BidAnswer(BidAnswerKind Kind, BidOutcome? Outcome, string? Reason, BidView? Standing, StandingVehicle? Vehicle, AuctionClock? Clock)
{
    /// <summary>An answer that took nothing.</summary>
    /// <param name="kind">Why.</param>
    /// <param name="reason">The rules' sentence, or null.</param>
    public static BidAnswer Refused(BidAnswerKind kind, string? reason) => new(kind, null, reason, null, null, null);
}
