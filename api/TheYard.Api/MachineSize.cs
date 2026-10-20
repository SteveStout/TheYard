// What the machine this process runs on has, read from the process itself: the processors the
// runtime was given and the memory the garbage collector may use, which on Linux is the cgroup's
// limit when one is set and the machine's otherwise. The plan card's size line reads these instead
// of a table of sizes by name, so a plan Azure resizes is described as it now is (ADR: The onion
// cut into vertical slices, the facts the Admin tab reads).
namespace TheYard.Api;

// #region machine-size
/// <summary>The processors and memory one instance of the plan gives this process.</summary>
/// <param name="Cores">The processors the runtime sees.</param>
/// <param name="MemoryGb">The memory the runtime may use, in gigabytes, to two places.</param>
public sealed record MachineSize(double Cores, double MemoryGb)
{
    /// <summary>The source the card names beside the figures.</summary>
    public const string Source = "read from this process: the processors the runtime sees and the memory its collector may use";

    /// <summary>This process's machine, read once.</summary>
    public static MachineSize OfThisProcess() =>
        new(Environment.ProcessorCount, Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024 / 1024, 2));
}
// #endregion machine-size
