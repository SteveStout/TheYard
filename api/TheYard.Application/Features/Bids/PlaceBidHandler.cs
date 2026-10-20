// A bid or a Buy Now, as one use case: is the session on this store, does the vehicle exist, does
// the domain accept the action on the server's clock, and what the caller sees afterwards.
using TheYard.Domain;

namespace TheYard.Application.Features.Bids;

// #region place-bid
/// <summary>
/// Places a bid, or buys now, for one signed-in bidder (ADR: The onion cut into vertical slices,
/// rule 7). The order of the three questions is the contract the browser relies on: the session's
/// store first, then the vehicle, then the rules. The auction is composed once per request by the
/// shared wiring and handed in; the clock is the server's.
/// </summary>
/// <param name="auction">The request's auction: catalogue, bids and room, composed in the order the rules need.</param>
/// <param name="clock">The server's clock, read once per request.</param>
public sealed class PlaceBidHandler(Auction auction, Func<AuctionClock> clock)
{
    /// <summary>Answers one bid or Buy Now.</summary>
    /// <param name="request">Who, on which vehicle, and the amount or none for Buy Now.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<BidAnswer> HandleAsync(PlaceBid request, CancellationToken cancellation)
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
        if (await auction.FindAsync(request.VehicleId) is not { } vehicle)
        {
            return BidAnswer.Refused(BidAnswerKind.NoSuchVehicle, null);
        }
        var outcome = request.Amount is { } amount
            ? await auction.PlaceBidAsync(vehicle, amount, now, request.UserId)
            : await auction.BuyNowAsync(vehicle, now, request.UserId);
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
            auction.BidsOf(request.UserId).TryGetValue(request.VehicleId, out var standing) ? standing : null,
            await auction.FindAsync(request.VehicleId) ?? vehicle,
            now);
    }
}
// #endregion place-bid

/// <summary>One bid or Buy Now.</summary>
/// <param name="VehicleId">The vehicle.</param>
/// <param name="UserId">The signed-in bidder.</param>
/// <param name="SessionOnThisStore">True when the session was opened on the store this request is on.</param>
/// <param name="Amount">Whole dollars for a bid; null for Buy Now.</param>
public sealed record PlaceBid(string VehicleId, string UserId, bool SessionOnThisStore, int? Amount);

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
