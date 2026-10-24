// The plan's children: what each app on the plan holds and burns now, and what the plan holds
// outside all of them. The owner asked for the four apps' RAM and CPU beside the plan's
// dial; the readings were already in the kept minutes (ADR: The App Service plan, read every
// minute), so this is arithmetic over them and nothing new is read.

namespace TheYard.Application.Shared.History;

/// <summary>Each app's newest memory and CPU from the plan's kept minutes, and the plan's memory outside the apps.</summary>
public static class PlanChildren
{
    /// <summary>The app's working set in the newest minute that has one, in MB; null when no minute does.</summary>
    public static double? MemoryMb(IReadOnlyList<PlanMinute> minutes, string app) =>
        Newest(minutes, app, entry => entry.WorkingSetMb);

    /// <summary>
    /// The app's CPU in the newest minute that has one, as a share of the plan's cores: the seconds
    /// of processor it used in the minute over the sixty seconds each core had.
    /// </summary>
    public static double? CpuPercent(IReadOnlyList<PlanMinute> minutes, string app, double? cores) =>
        cores is > 0 && Newest(minutes, app, entry => entry.CpuSeconds) is { } seconds
            ? Math.Round(seconds / 60 / cores.Value * 100, 1)
            : null;

    /// <summary>
    /// The plan's memory that no app's working set accounts for, in MB, in the newest minute where
    /// the plan's memory and every app's were all read: the operating system, the platform's own
    /// containers beside each site, and the file cache.
    /// </summary>
    public static double? OutsideMb(IReadOnlyList<PlanMinute> minutes, double? memoryGb)
    {
        if (memoryGb is not > 0)
        {
            return null;
        }
        for (int i = minutes.Count - 1; i >= 0; i--)
        {
            var minute = minutes[i];
            if (minute.MemoryPercent is { } percent && minute.Apps.Count > 0 && minute.Apps.All(app => app.WorkingSetMb is not null))
            {
                double plan = memoryGb.Value * 1024 * percent / 100;
                return Math.Round(Math.Max(0, plan - minute.Apps.Sum(app => app.WorkingSetMb!.Value)));
            }
        }
        return null;
    }

    private static double? Newest(IReadOnlyList<PlanMinute> minutes, string app, Func<PlanAppMinute, double?> pick)
    {
        for (int i = minutes.Count - 1; i >= 0; i--)
        {
            var entry = minutes[i].Apps.FirstOrDefault(candidate => string.Equals(candidate.Name, app, StringComparison.OrdinalIgnoreCase));
            if (entry is not null && pick(entry) is { } value)
            {
                return value;
            }
        }
        return null;
    }
}
