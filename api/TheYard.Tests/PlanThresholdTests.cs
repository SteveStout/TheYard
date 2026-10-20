using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// When the plan card turns amber or red (ADR: The App Service plan, read every minute): CPU 60
/// and 80, memory 70 and 85, the HTTP queue 5 and 20, sockets at 60 per cent of 1,920, each held
/// for fifteen minutes; red at once for a stopped app or a restart nobody asked for.
/// </summary>
public class PlanThresholdTests
{
    private static IReadOnlyList<double?> Held(double value, int minutes = 15) => Enumerable.Repeat<double?>(value, minutes).ToList();

    // #region plan-threshold-tests
    [Theory]
    [InlineData(59, PlanTone.Good)]
    [InlineData(60, PlanTone.Warning)]
    [InlineData(80, PlanTone.Alarm)]
    public void Cpu_turns_at_sixty_and_eighty(double value, PlanTone tone) =>
        Assert.Equal(tone, PlanThresholds.ToneOf(Held(value), PlanThresholds.Cpu));

    [Theory]
    [InlineData(69, PlanTone.Good)]
    [InlineData(70, PlanTone.Warning)]
    [InlineData(85, PlanTone.Alarm)]
    public void Memory_turns_at_seventy_and_eighty_five(double value, PlanTone tone) =>
        Assert.Equal(tone, PlanThresholds.ToneOf(Held(value), PlanThresholds.Memory));

    [Fact]
    public void A_level_counts_only_when_every_one_of_fifteen_minutes_holds_it()
    {
        var spike = Held(95, 14).Prepend(10).ToList();
        Assert.Equal(PlanTone.Good, PlanThresholds.ToneOf(spike, PlanThresholds.Cpu));
        Assert.Equal(PlanTone.Info, PlanThresholds.ToneOf(Held(95, 14), PlanThresholds.Cpu));
        Assert.Equal(PlanTone.Alarm, PlanThresholds.ToneOf(Held(20, 15), PlanThresholds.HttpQueue));
        Assert.Equal(PlanTone.Warning, PlanThresholds.SocketsTone(Held(1_152), PlanThresholds.SocketLimitFor("B1")));
        Assert.Equal(PlanTone.Good, PlanThresholds.SocketsTone(Held(1_151), PlanThresholds.SocketLimitFor("B1")));
        // The plan is B2, whose limit is 3,968 (1.0.4.0 measured every size against B1's 1,920).
        Assert.Equal(PlanTone.Good, PlanThresholds.SocketsTone(Held(1_152), PlanThresholds.SocketLimitFor("B2")));
        Assert.Equal(PlanTone.Warning, PlanThresholds.SocketsTone(Held(2_381), PlanThresholds.SocketLimitFor("b2")));
        Assert.Equal(PlanTone.Info, PlanThresholds.SocketsTone(Held(9_999), PlanThresholds.SocketLimitFor("EP1")));
        Assert.Null(PlanThresholds.SocketLimitFor(null));
    }

    [Fact]
    public void A_stopped_app_or_an_unplanned_restart_is_red_at_once()
    {
        var at = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(PlanTone.Alarm, PlanThresholds.AppsTone([new PlanAppState("a", "/a", "Stopped", true, null)]));
        Assert.Equal(PlanTone.Good, PlanThresholds.AppsTone([new PlanAppState("a", "/a", "Running", true, null)]));
        Assert.Equal(PlanTone.Alarm, PlanThresholds.EventsTone([new PlanEvent(at, "restart", "Restart", "a", Planned: false)]));
        Assert.Equal(PlanTone.Info, PlanThresholds.EventsTone([new PlanEvent(at, "restart", "Restart", "a", Planned: true)]));
    }
    // #endregion plan-threshold-tests
}
