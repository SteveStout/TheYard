// What the machines are doing, read for the Admin tab. This file holds the container's own
// sampler and its reading; the other parts each have a file of their own beside it:
// TheYard.Infrastructure/ResourceStats.cs (the relational store's resource view), TrafficMinutes.cs (the request ring a
// minute at a time), MachineRecorder.cs (the minute kept for the day, week and month, and the reader
// that serves them back) and DocumentLoad.cs (the document store's request units).
using System.Diagnostics;

namespace TheYard.Api;

// #region machine-sampler
/// <summary>
/// What the container is doing to itself, sampled on a timer and kept in a
/// ring (ADR: What the machines are doing). Every other reading on the Admin
/// tab is taken when something happens: a request, a statement, an operation.
/// Memory is not an event, so nothing on this page could show it until
/// something asked at a fixed interval, which is what this does.
///
/// <para>Fifteen seconds, two hundred and forty of them, which is the last
/// hour and about twenty kilobytes of this container's memory. The ring empties
/// on every roll like every other ring here, and the card says so rather than
/// implying a history the container does not keep.</para>
/// </summary>
public sealed class MachineSampler(int capacity) : BackgroundService
{
    /// <summary>How often a reading is taken: every fifteen seconds.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    /// <summary>How many readings the ring keeps, never fewer than one.</summary>
    private readonly int _capacity = Math.Max(1, capacity);

    /// <summary>The lock that guards the ring, since the timer writes it while requests read it.</summary>
    private readonly object _gate = new();

    /// <summary>The readings, oldest first.</summary>
    private readonly Queue<MachineSample> _samples = new();

    /// <summary>The processor time the process had used at the last reading, to subtract from the next.</summary>
    private TimeSpan _lastCpu = TimeSpan.Zero;

    /// <summary>When the last reading was taken; the minimum value until the first one.</summary>
    private DateTimeOffset _lastAt = DateTimeOffset.MinValue;

    /// <summary>What the runtime says this container may use, which is what a memory number here is a share of.</summary>
    public static double MemoryLimitMb => Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d, 1);

    /// <summary>The processors this process can use, as the runtime sees them inside the container.</summary>
    public static int Processors => Environment.ProcessorCount;

    /// <summary>A copy of the ring as it stands, oldest first, safe to read while the timer keeps writing.</summary>
    public IReadOnlyList<MachineSample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }

    /// <summary>One reading. Public so the suite can take one without waiting a quarter of a minute for the timer.</summary>
    public MachineSample Sample()
    {
        using var process = Process.GetCurrentProcess();
        var at = DateTimeOffset.UtcNow;
        var cpu = process.TotalProcessorTime;
        var info = GC.GetGCMemoryInfo();

        // The share of the container's processors this process spent since the
        // last reading. The first reading has nothing to subtract from and
        // reports no percentage rather than one averaged over the whole life
        // of the process, which is a different number wearing the same label.
        double? percent = null;
        if (_lastAt != DateTimeOffset.MinValue)
        {
            double elapsed = (at - _lastAt).TotalMilliseconds;
            if (elapsed > 0)
            {
                percent = Math.Round((cpu - _lastCpu).TotalMilliseconds / (elapsed * Processors) * 100, 1);
            }
        }
        _lastCpu = cpu;
        _lastAt = at;

        var sample = new MachineSample(
            at,
            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1),
            Math.Round(info.HeapSizeBytes / 1024d / 1024d, 1),
            percent,
            process.Threads.Count,
            GC.CollectionCount(0),
            GC.CollectionCount(2));

        lock (_gate)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > _capacity)
            {
                _samples.Dequeue();
            }
        }
        return sample;
    }

    /// <summary>The timer: one reading at start, then one every fifteen seconds until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(Every);
        Sample();
        while (await timer.WaitForNextTickAsync(stopping))
        {
            Sample();
        }
    }
}

/// <summary>One reading of the container: memory as the operating system and the runtime each see it, the processor share since the last reading, and what the collector has done.</summary>
/// <param name="At">When the reading was taken, UTC.</param>
/// <param name="WorkingSetMb">The process working set as the operating system sees it, in megabytes.</param>
/// <param name="ManagedMb">Memory the runtime counts as allocated to managed objects, in megabytes.</param>
/// <param name="HeapMb">The size of the managed heap after the last collection, in megabytes.</param>
/// <param name="CpuPercent">The share of the container's processors spent since the last reading, as a percentage; null on the first reading.</param>
/// <param name="Threads">How many threads the process has.</param>
/// <param name="Gen0Collections">How many generation 0 collections have run since the process started.</param>
/// <param name="Gen2Collections">How many generation 2 collections have run since the process started.</param>
public sealed record MachineSample(
    DateTimeOffset At,
    double WorkingSetMb,
    double ManagedMb,
    double HeapMb,
    double? CpuPercent,
    int Threads,
    int Gen0Collections,
    int Gen2Collections);
// #endregion machine-sampler
