namespace TheBlock.Domain;

/// <summary>
/// The two instants auction scheduling needs: the current moment, and the
/// buyer's local midnight the schedule anchors to. Carrying the anchor
/// explicitly (the client sends its own) sidesteps every timezone and DST
/// disagreement between the browser's clock and the server's.
/// </summary>
public readonly record struct AuctionClock(long NowMs, long AnchorMs)
{
    /// <summary>Fallback when no client anchor was provided: midnight in the given zone (DST-correct).</summary>
    public static AuctionClock ServerLocal(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        DateTime localDate = TimeZoneInfo.ConvertTime(utcNow, zone).Date;
        var midnight = new DateTimeOffset(localDate, zone.GetUtcOffset(localDate));
        return new AuctionClock(utcNow.ToUnixTimeMilliseconds(), midnight.ToUnixTimeMilliseconds());
    }
}
