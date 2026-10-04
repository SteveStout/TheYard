namespace TheYard.Domain;

/// <summary>
/// The two instants auction scheduling needs: the current moment, and the
/// midnight the schedule anchors to. The anchor is the server's, the current
/// UTC day's midnight, so every visitor is in the same auction and no request
/// can name a day. An anchor the caller chose would put two visitors in
/// different zones in different auctions, and would let a caller that sent
/// yesterday's midnight bid on a vehicle that has ended for everyone else.
/// </summary>
/// <param name="NowMs">The current moment, as Unix epoch milliseconds.</param>
/// <param name="AnchorMs">The UTC midnight the schedule anchors to, as Unix epoch milliseconds.</param>
public readonly record struct AuctionClock(long NowMs, long AnchorMs)
{
    // #region utc
    /// <summary>The clock for a moment: that moment, and the UTC midnight that began its day.</summary>
    public static AuctionClock Utc(DateTimeOffset utcNow)
    {
        var midnight = new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero);
        return new AuctionClock(utcNow.ToUnixTimeMilliseconds(), midnight.ToUnixTimeMilliseconds());
    }
    // #endregion utc
}
