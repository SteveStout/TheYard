namespace TheBlock.Domain;

public enum AuctionStatus
{
    Upcoming,
    Live,
    Ended,
}

/// <summary>An auction's open/close instants as Unix epoch milliseconds.</summary>
public readonly record struct AuctionWindow(long StartsAtMs, long EndsAtMs);

/// <summary>
/// Derives each vehicle's auction window from its id — the same math as the
/// frontend's src/lib/auction.ts (FNV-1a seed, end times spread across the
/// two days before through five days after the midnight anchor, 2–4 day
/// runs), so server-side status filtering agrees with what the client renders.
/// </summary>
public static class AuctionSchedule
{
    private const long HourMs = 60L * 60 * 1000;
    private const long DayMs = 24 * HourMs;

    /// <summary>End times land within [anchor - Lookback, anchor - Lookback + Span).</summary>
    private const long ScheduleLookbackMs = 2 * DayMs;
    private const long ScheduleSpanMs = 7 * DayMs;

    /// <summary>Each auction runs 2–4 days from open to close.</summary>
    private const long MinDurationMs = 2 * DayMs;
    private const long ExtraDurationMs = 2 * DayMs;

    /// <summary>Deterministic per id and anchor (the buyer's local midnight).</summary>
    public static AuctionWindow Window(string vehicleId, long anchorMs)
    {
        uint hash = Fnv1a.Hash(vehicleId);
        double endFraction = hash % 100_000 / 100_000.0;
        double durationFraction = hash / 100_000 % 1_000 / 1_000.0;
        long endsAt = (long)Math.Round(anchorMs - ScheduleLookbackMs + endFraction * ScheduleSpanMs);
        long durationMs = (long)Math.Round(MinDurationMs + durationFraction * ExtraDurationMs);
        return new AuctionWindow(endsAt - durationMs, endsAt);
    }

    /// <summary>Live from StartsAt (inclusive) until EndsAt (exclusive).</summary>
    public static AuctionStatus Status(AuctionWindow window, long nowMs)
    {
        if (nowMs < window.StartsAtMs)
        {
            return AuctionStatus.Upcoming;
        }
        return nowMs < window.EndsAtMs ? AuctionStatus.Live : AuctionStatus.Ended;
    }

    public static AuctionStatus StatusFor(string vehicleId, AuctionClock clock) =>
        Status(Window(vehicleId, clock.AnchorMs), clock.NowMs);
}
