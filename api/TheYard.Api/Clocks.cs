using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// The AuctionClock for a request: now, and the UTC midnight that began the
/// day, the same for every caller. Until 1.0.0.112 the caller sent its own
/// local midnight as <c>anchor_ms</c> and this resolved it, which made the
/// auction a function of who was looking; the parameter is gone from the API
/// and ignored if an old page still sends it (ADR: Three readers with no
/// memory of the project, the addendum on the clock).
/// </summary>
public static class Clocks
{
    public static AuctionClock Now() => AuctionClock.Utc(DateTimeOffset.UtcNow);
}
