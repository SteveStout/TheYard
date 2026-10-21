using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The nightly lane's slot table (ADR: The Nightly Function on the Admin tab): which Central hour
/// is which run, on an ordinary day and on both daylight-saving days; the slots due in a window and
/// the next one; how a month of five-minute firing counts is judged on time, late or missed; and
/// the Function's clock held to the same file, so the card and the Function read one table.
/// </summary>
public sealed class NightlySlotsTests
{
    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

    // #region nightly-slot-tests
    /// <summary>A day on Central daylight time, five hours behind UTC: every slot hour and three that are not.</summary>
    [Theory]
    [InlineData("2026-10-20T05:00:00Z", "bookkeeping")]
    [InlineData("2026-10-20T09:00:00Z", "nightly")]
    [InlineData("2026-10-20T13:00:00Z", "bookkeeping")]
    [InlineData("2026-10-20T17:00:00Z", "bookkeeping")]
    [InlineData("2026-10-20T21:00:00Z", "bookkeeping")]
    [InlineData("2026-10-21T01:00:00Z", "bookkeeping")]
    [InlineData("2026-10-20T08:00:00Z", null)]
    [InlineData("2026-10-20T10:00:00Z", null)]
    [InlineData("2026-10-20T18:00:00Z", null)]
    public void Each_central_hour_is_its_slot_or_none(string utc, string? slot) =>
        Assert.Equal(slot, NightlySlots.Slot(At(utc)));

    /// <summary>
    /// The two days the offset moves: in November the 4 AM slot is an hour later in UTC than the
    /// day before, and in March an hour earlier; the hour that repeats or vanishes is never a slot.
    /// </summary>
    [Theory]
    [InlineData("2026-11-01T05:00:00Z", "bookkeeping")]
    [InlineData("2026-11-01T06:00:00Z", null)]
    [InlineData("2026-11-01T07:00:00Z", null)]
    [InlineData("2026-11-01T09:00:00Z", null)]
    [InlineData("2026-11-01T10:00:00Z", "nightly")]
    [InlineData("2026-03-08T09:00:00Z", "nightly")]
    [InlineData("2026-03-08T10:00:00Z", null)]
    public void The_slots_follow_central_time_across_daylight_saving(string utc, string? slot) =>
        Assert.Equal(slot, NightlySlots.Slot(At(utc)));

    [Fact]
    public void A_day_holds_six_slots_one_of_them_the_reply_lane_and_a_part_hour_start_is_not_counted()
    {
        var day = NightlySlots.Due(At("2026-10-20T00:00:00Z"), At("2026-10-21T00:00:00Z"));
        Assert.Equal(6, day.Count);
        Assert.Equal(new[] { At("2026-10-20T09:00:00Z") }, day.Where(d => d.Slot == NightlySlots.Reply).Select(d => d.At));
        Assert.True(day.SequenceEqual(day.OrderBy(d => d.At)), "the slots come oldest first");
        var fromHalfPast = NightlySlots.Due(At("2026-10-20T05:30:00Z"), At("2026-10-20T13:00:00Z"));
        Assert.Equal(new[] { At("2026-10-20T09:00:00Z") }, fromHalfPast.Select(d => d.At));
        Assert.Empty(NightlySlots.Due(At("2026-10-20T09:00:00Z"), At("2026-10-20T09:00:00Z")));
    }

    [Fact]
    public void The_next_slot_is_strictly_after_the_moment_asked()
    {
        Assert.Equal((At("2026-10-20T09:00:00Z"), NightlySlots.Reply), NightlySlots.Next(At("2026-10-20T08:59:00Z")));
        Assert.Equal((At("2026-10-20T13:00:00Z"), NightlySlots.Bookkeeping), NightlySlots.Next(At("2026-10-20T09:00:00Z")));
        Assert.Equal(At("2026-11-01T10:00:00Z"), NightlySlots.Next(At("2026-11-01T05:00:00Z")).At);
    }

    [Fact]
    public void A_slot_is_on_time_in_its_first_five_minutes_late_later_in_its_hour_and_missed_otherwise()
    {
        var due = NightlySlots.Due(At("2026-10-20T09:00:00Z"), At("2026-10-20T18:00:00Z"));
        (DateTimeOffset, double)[] executions =
        [
            (At("2026-10-20T08:55:00Z"), 1),
            (At("2026-10-20T09:00:00Z"), 1),
            (At("2026-10-20T13:00:00Z"), 0),
            (At("2026-10-20T13:20:00Z"), 2),
            (At("2026-10-20T18:00:00Z"), 1),
        ];
        var tally = NightlySlots.Tally(due, executions);
        Assert.Equal(new SlotTally(3, 1, 1, 1), tally);
        Assert.Equal(33.3, tally.OnTimePercent);
        Assert.Equal((NightlySlots.Late, (DateTimeOffset?)At("2026-10-20T13:20:00Z")), NightlySlots.Judge(At("2026-10-20T13:00:00Z"), executions));
        Assert.Equal((NightlySlots.Missed, (DateTimeOffset?)null), NightlySlots.Judge(At("2026-10-20T17:00:00Z"), executions));
        Assert.Null(NightlySlots.Tally([], executions).OnTimePercent);
    }

    [Fact]
    public void The_central_day_starts_at_central_midnight()
    {
        Assert.Equal(At("2026-10-20T05:00:00Z"), NightlySlots.StartOfCentralDay(At("2026-10-20T23:00:00Z")));
        Assert.Equal(At("2026-11-02T06:00:00Z"), NightlySlots.StartOfCentralDay(At("2026-11-02T12:00:00Z")));
    }

    /// <summary>
    /// The Function compiles this exact file by a link in its project, and its clock asks the table
    /// rather than keeping hours of its own, so the card's idea of a slot is the Function's.
    /// </summary>
    [Fact]
    public void The_function_compiles_the_same_table_and_keeps_no_hours_of_its_own()
    {
        string folder = Path.Combine(Repo.Root(), "functions", "jobhunt");
        string project = File.ReadAllText(Path.Combine(folder, "JobHunt.Functions.csproj"));
        string clock = File.ReadAllText(Path.Combine(folder, "CentralClock.cs"));
        Assert.Contains("<Compile Include=\"..\\..\\api\\TheYard.Domain\\NightlySlots.cs\" Link=\"NightlySlots.cs\" />", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Repo.Root(), "api", "TheYard.Domain", "NightlySlots.cs")));
        Assert.Contains("NightlySlots.Slot(", clock, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", clock, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hour", clock, StringComparison.Ordinal);
        Assert.DoesNotContain("FindSystemTimeZoneById", clock, StringComparison.Ordinal);
    }
    // #endregion nightly-slot-tests
}
