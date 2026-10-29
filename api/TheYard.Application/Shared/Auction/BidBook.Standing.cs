// Where a bid is decided against: the standing the store holds for one vehicle, and the next
// standing a bid would leave. It has a file of its own because these are the two pieces the bid
// path and the reset share, and the reasoning about what the store is trusted with lives here
// (the type itself is in BidBook.cs).
namespace TheYard.Application.Shared.Auction;

/// <summary>The store's standing for one vehicle, and how a bid moves it.</summary>
public sealed partial class BidBook
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
    /// <param name="vehicleId">The vehicle whose standing is read.</param>
    /// <param name="cancellation">The caller's, on the two reads; both callers hold the gate, and
    /// the write that follows does not take it (ADR: The ports learn to wait, the addendum on the token).</param>
    private async Task<(VehicleStanding? Held, string? Version)> CurrentAsync(string vehicleId, CancellationToken cancellation)
    {
        var stored = await _store.ReadStandingAsync(vehicleId, cancellation);
        var held = stored is not null ? stored.Standing : StandingFold.Derive(await _store.BidsOnAsync(vehicleId, cancellation));
        _ledger.Hold(vehicleId, held);
        return (held, stored?.Version);
    }
    // #endregion current
}
