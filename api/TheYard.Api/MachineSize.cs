// What the machine this process runs on has, read from the process itself: the processors the
// runtime was given, and the machine's memory as Linux states it, the cgroup's limit when one is set
// and /proc/meminfo's total otherwise. Not the garbage collector's figure: the web app caps its own
// heap (DOTNET_GCHeapHardLimit), so that figure is the cap, not the machine. The plan card's size
// line reads these instead of a table of sizes by name, so a plan Azure resizes is described as it now is (ADR: The onion
// cut into vertical slices, the facts the Admin tab reads).
namespace TheYard.Api;

// #region machine-size
/// <summary>The processors and memory one instance of the plan gives this process.</summary>
/// <param name="Cores">The processors the runtime sees.</param>
/// <param name="MemoryGb">The machine's memory, in gigabytes, to two places.</param>
public sealed record MachineSize(double Cores, double MemoryGb)
{
    /// <summary>The source the card names beside the figures.</summary>
    public const string Source = "read from this process: the processors the runtime sees and the memory Linux states";

    /// <summary>This process's machine, read once; the collector's figure only where Linux states none.</summary>
    public static MachineSize OfThisProcess() =>
        new(Environment.ProcessorCount, MemoryGbOf(ReadOrNull("/sys/fs/cgroup/memory.max"), ReadOrNull("/proc/meminfo"))
            ?? Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024 / 1024, 2));

    /// <summary>The memory Linux states: the cgroup's limit when it is a number, else meminfo's MemTotal; null when neither says.</summary>
    /// <param name="cgroupMax">The text of memory.max, or null.</param>
    /// <param name="meminfo">The text of /proc/meminfo, or null.</param>
    public static double? MemoryGbOf(string? cgroupMax, string? meminfo)
    {
        if (long.TryParse(cgroupMax?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long bytes) && bytes > 0)
        {
            return Math.Round(bytes / 1024d / 1024 / 1024, 2);
        }
        var total = meminfo?.Split('\n').FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.Ordinal));
        return total is not null && long.TryParse(total["MemTotal:".Length..].Replace("kB", "", StringComparison.Ordinal).Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long kb)
            ? Math.Round(kb / 1024d / 1024, 2)
            : null;
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
// #endregion machine-size
