using TheBlock.Domain;

namespace TheBlock.Api;

/// <summary>
/// Resolves the AuctionClock for a request. The client sends its own
/// local-midnight anchor so server-side scheduling agrees with the browser's
/// rendering across timezones and DST; without one, the server's local
/// midnight is the fallback.
/// </summary>
public static class Clocks
{
    /// <summary>A real client's midnight anchor is always within a day or two of now.</summary>
    private const long MaxAnchorDriftMs = 2L * 24 * 60 * 60 * 1000;

    public static bool TryResolve(long? anchorMs, out AuctionClock clock, out string? error)
    {
        var utcNow = DateTimeOffset.UtcNow;
        if (anchorMs is { } anchor)
        {
            if (Math.Abs(anchor - utcNow.ToUnixTimeMilliseconds()) > MaxAnchorDriftMs)
            {
                clock = default;
                error = "anchor_ms must be within two days of the current time.";
                return false;
            }
            clock = new AuctionClock(utcNow.ToUnixTimeMilliseconds(), anchor);
        }
        else
        {
            clock = AuctionClock.ServerLocal(utcNow, TimeZoneInfo.Local);
        }
        error = null;
        return true;
    }
}
