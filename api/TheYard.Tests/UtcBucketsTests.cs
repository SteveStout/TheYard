using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The UTC day, hour and minute every kept reading is filed under (UtcBuckets in Application).
/// One copy in the code, so the activity, cost and machine windows always agree on where an
/// instant falls.
/// </summary>
public sealed class UtcBucketsTests
{
    [Fact]
    public void An_instant_falls_in_its_UTC_day_hour_and_minute_whatever_its_offset()
    {
        var late = new DateTimeOffset(2026, 10, 3, 21, 47, 33, TimeSpan.FromHours(-5));

        Assert.Equal("2026-10-04", late.UtcDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero), late.UtcHour);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 2, 47, 0, TimeSpan.Zero), late.UtcMinute);
    }
}
