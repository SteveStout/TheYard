using TheYard.Domain;

namespace TheYard.Api;

/// <summary>
/// The AuctionClock for a request: now, and the UTC midnight that began the
/// day, the same for every caller. A request never names a day, because an
/// auction that depended on the caller's clock would be a different auction
/// for every visitor.
/// </summary>
public static class Clocks
{
    public static AuctionClock Now() => AuctionClock.Utc(DateTimeOffset.UtcNow);
}
