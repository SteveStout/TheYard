using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The plan's children (ADR: The App Service plan, read every minute, the addendum on the apps):
/// each app's newest memory and CPU, and the plan's memory no app accounts for, from the minutes
/// the plan already keeps.
/// </summary>
public class PlanChildrenTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);

    private static PlanMinute Minute(int i, double? memoryPercent, params PlanAppMinute[] apps) =>
        new(At.AddMinutes(i), 5, memoryPercent, 0, 0, 0, 0, 0, "1.0.3.181", apps);

    [Fact]
    public void Each_app_reads_its_newest_memory_and_its_cpu_as_a_share_of_the_plans_cores()
    {
        var minutes = new[]
        {
            Minute(0, 70, new PlanAppMinute("APP-A", 6, 380, 10, 0, null)),
            Minute(1, 70, new PlanAppMinute("APP-A", null, null, 10, 0, null)),
        };

        Assert.Equal(380, PlanChildren.MemoryMb(minutes, "app-a"));
        // Six seconds of processor in a minute on two cores is five per cent of the plan.
        Assert.Equal(5.0, PlanChildren.CpuPercent(minutes, "APP-A", 2));
        Assert.Null(PlanChildren.CpuPercent(minutes, "APP-A", null));
        Assert.Null(PlanChildren.MemoryMb(minutes, "APP-NOT-HERE"));
    }

    [Fact]
    public void The_plan_outside_the_apps_is_its_memory_less_every_apps_working_set()
    {
        var minutes = new[]
        {
            Minute(0, 70, new PlanAppMinute("A", 1, 400, 0, 0, null), new PlanAppMinute("B", 1, 100, 0, 0, null)),
            // A minute where one app's memory was not read is not used: it would count that app as outside.
            Minute(1, 71, new PlanAppMinute("A", 1, 400, 0, 0, null), new PlanAppMinute("B", 1, null, 0, 0, null)),
        };

        // 70 per cent of 3.5 GB is 2,508.8 MB, less 500 MB of apps.
        Assert.Equal(2009, PlanChildren.OutsideMb(minutes, 3.5));
        Assert.Null(PlanChildren.OutsideMb(minutes, null));
        Assert.Null(PlanChildren.OutsideMb([], 3.5));
    }
}
