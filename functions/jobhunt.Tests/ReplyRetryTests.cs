using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The reply lane's second chance (PLAN-AUTOHUNT-2026-11-09 rung 2): on 8 November the 4 AM dispatch reached GitHub
/// and the runner's setup died on a 504 before the lane began, so no queue was written and the 6 AM messages lane read
/// the day before's. The 5 o'clock hour in Central dispatches the reply lane once more when no reply run of the day
/// holds linkedin-queue.md, and is the plain "not a slot" hour otherwise.
/// </summary>
public sealed class ReplyRetryTests
{
    [Fact]
    public void The_retry_hour_is_five_o_clock_Central_on_both_sides_of_the_clock_change_and_is_not_a_slot()
    {
        var standard = new DateTimeOffset(2026, 11, 9, 11, 0, 0, TimeSpan.Zero);
        var daylight = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero);

        Assert.True(CentralClock.IsReplyRetryHour(standard));
        Assert.True(CentralClock.IsReplyRetryHour(daylight));
        Assert.False(CentralClock.IsReplyRetryHour(standard.AddHours(-1)));
        Assert.Null(CentralClock.Slot(standard));
        Assert.Equal("NIGHTLY_DISPATCH", Dispatch.Gate(CentralClock.Reply));
    }

    [Fact]
    public void A_day_whose_only_reply_run_wrote_no_queue_needs_the_retry()
    {
        Assert.True(ReplyRetry.Needed([("0400-reply-opus-5-5", false)]));
        Assert.True(ReplyRetry.Needed([]));
    }

    [Fact]
    public void A_queue_from_any_real_slot_is_enough_and_a_dry_run_s_queue_is_not()
    {
        Assert.False(ReplyRetry.Needed([("0400-reply-opus-5-5", true)]));
        Assert.False(ReplyRetry.Needed([("0400-reply-opus-5-5", false), ("1130-reply-opus-5-5", true)]));
        Assert.True(ReplyRetry.Needed([("0400-reply-opus-5-5-dry", true)]));
        Assert.False(ReplyRetry.IsRun("0600-messages-sonnet-5-5"));
    }
}
