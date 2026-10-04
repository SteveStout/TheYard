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
    /// <summary>The auction's clock for this request: now, and the UTC midnight that began the day.</summary>
    public static AuctionClock Now() => AuctionClock.Utc(UtcNow());

    /// <summary>
    /// The server's UTC clock, the one place an endpoint reads the time, so a handler that
    /// stamps a record or picks a reporting window reads the same clock the auction does.
    /// </summary>
    public static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
