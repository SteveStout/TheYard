// Where a bid is decided against: the standing the store holds for one vehicle, and the next
// standing a bid would leave. It has a file of its own because these are the two pieces the bid
// path and the reset share, and the reasoning about what the store is trusted with lives here
// (the type itself is in BidService.cs).
namespace TheYard.Application;

/// <summary>The store's standing for one vehicle, and how a bid moves it.</summary>
public sealed partial class BidService
{
    /// <summary>
    /// How many times a bid reads the store and runs the rules before it gives
    /// up. One gate per process means a second attempt is only ever needed when
    /// the other site wrote the same vehicle in between, so three is plenty.
    /// </summary>
    private const int Attempts = 3;

    /// <summary>What a buyer is told when the other site kept moving the vehicle for every attempt.</summary>
    public const string MovedReason = "The price moved while this bid was being placed. Look again and bid.";

    // #region current
    /// <summary>
    /// The vehicle's standing as the store holds it right now, with the version
    /// a write has to name, and the cache brought up to the same fact. When
    /// the store holds no standing the rows decide it, and the version is null,
    /// which a write reads as "there was nothing here": if another writer
    /// creates one in between, the write is refused like any other conflict.
    /// </summary>
    private async Task<(VehicleStanding? Held, string? Version)> CurrentAsync(string vehicleId)
    {
        var stored = await _store.ReadStandingAsync(vehicleId);
        var held = stored is not null ? stored.Standing : Derive(await _store.BidsOnAsync(vehicleId));
        if (held is null)
        {
            _standing.TryRemove(vehicleId, out _);
        }
        else
        {
            _standing[vehicleId] = held;
        }
        return (held, stored?.Version);
    }
    // #endregion current

    // #region next
    /// <summary>
    /// The standing one accepted bid leaves. The price only moves up: a bid at
    /// or under the standing price that got this far is a buy-now at a price
    /// the room had already passed, and it marks the vehicle sold without
    /// taking the lead from whoever holds it. The count never drops, and sold
    /// never comes undone.
    /// </summary>
    private static VehicleStanding Next(VehicleStanding? held, string userId, BidState state) =>
        held is null
            ? new VehicleStanding(state.Amount, state.BidCount, userId, state.WonBuyNow, state.AtMs)
            : state.Amount > held.Amount
                ? new VehicleStanding(
                    state.Amount,
                    Math.Max(held.BidCount, state.BidCount),
                    userId,
                    held.SoldBuyNow || state.WonBuyNow,
                    state.AtMs)
                : held with
                {
                    BidCount = Math.Max(held.BidCount, state.BidCount),
                    SoldBuyNow = held.SoldBuyNow || state.WonBuyNow,
                };

    /// <summary>
    /// The standing a set of buyer rows adds up to, oldest first through
    /// <see cref="Next"/>, so a tie on price goes to whoever got there first.
    /// Null when there are no rows at all.
    /// </summary>
    private static VehicleStanding? Derive(IEnumerable<StoredBid> bids)
    {
        VehicleStanding? held = null;
        foreach (var bid in bids.OrderBy(bid => bid.State.AtMs).ThenBy(bid => bid.UserId, StringComparer.Ordinal))
        {
            held = Next(held, bid.UserId, bid.State);
        }
        return held;
    }
    // #endregion next
}
