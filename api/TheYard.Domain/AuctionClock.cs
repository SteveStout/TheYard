namespace TheYard.Domain;

/// <summary>
/// The two instants auction scheduling needs: the current moment, and the
/// midnight the schedule anchors to. The anchor is the server's, the current
/// UTC day's midnight, so every visitor is in the same auction and no request
/// can name a day. It was the caller's local midnight until 1.0.0.112, which
/// put two visitors in different zones in different auctions and let a client
/// that sent yesterday's midnight bid on a vehicle that had ended for everyone
/// else (ADR: Three readers with no memory of the project, the addendum on
/// the clock).
/// </summary>
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
