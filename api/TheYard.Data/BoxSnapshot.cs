namespace TheYard.Data;

// #region box-snapshot
/// <summary>
/// One reading of the box that supports the job hunt, as its own agent posts it once a minute: every
/// core, the memory and swap, each interface, each file system and the array under it, each GPU, every
/// temperature with its own marks, and the services the job hunt runs on (ADR: The box reports itself).
/// The wire is snake_case under these names; an address or a port may be in the post and never reaches a card.
/// </summary>
/// <param name="Host">The box's host name.</param>
/// <param name="Kernel">The running kernel's release.</param>
/// <param name="Distro">The distribution and its version, as /etc/os-release names it.</param>
/// <param name="BootedAt">When the box booted.</param>
/// <param name="UptimeS">Seconds since boot.</param>
/// <param name="Loadavg">The load averages over 1, 5 and 15 minutes.</param>
/// <param name="Cores">Each core.</param>
/// <param name="Memory">The memory.</param>
/// <param name="Swap">The swap.</param>
/// <param name="Nets">Each network interface.</param>
/// <param name="Disks">Each real file system.</param>
/// <param name="Gpus">Each GPU.</param>
/// <param name="Temps">Each temperature sensor.</param>
/// <param name="Services">Each service the job hunt runs on.</param>
/// <param name="AgentVersion">The agent's own version.</param>
/// <param name="AgentCpuPercent">The agent's share of one core over the last minute.</param>
/// <param name="At">When the snapshot was read, by the box's clock.</param>
public sealed record BoxSnapshot(
    string Host,
    string Kernel,
    string Distro,
    DateTimeOffset BootedAt,
    long UptimeS,
    IReadOnlyList<double> Loadavg,
    IReadOnlyList<BoxCore> Cores,
    BoxMemory Memory,
    BoxSwap Swap,
    IReadOnlyList<BoxNet> Nets,
    IReadOnlyList<BoxDisk> Disks,
    IReadOnlyList<BoxGpu> Gpus,
    IReadOnlyList<BoxTemp> Temps,
    IReadOnlyList<BoxService> Services,
    string AgentVersion,
    double AgentCpuPercent,
    DateTimeOffset At);

/// <summary>One core.</summary>
/// <param name="Index">The core's number, from zero.</param>
/// <param name="Load">Its busy share, 0 to 100.</param>
/// <param name="Mhz">Its clock.</param>
/// <param name="Temp">Its temperature, when a sensor names it.</param>
public sealed record BoxCore(int Index, double Load, double Mhz, double? Temp);

/// <summary>The memory, in bytes.</summary>
/// <param name="TotalB">All of it.</param>
/// <param name="UsedB">What programs hold.</param>
/// <param name="CacheB">What the kernel holds as cache and buffers.</param>
/// <param name="FreeB">What nothing holds.</param>
public sealed record BoxMemory(long TotalB, long UsedB, long CacheB, long FreeB);

/// <summary>The swap, in bytes.</summary>
/// <param name="TotalB">All of it.</param>
/// <param name="UsedB">What is in use.</param>
public sealed record BoxSwap(long TotalB, long UsedB);

/// <summary>One network interface.</summary>
/// <param name="Name">The interface's name.</param>
/// <param name="InBps">Bytes a second received, over the last tick.</param>
/// <param name="OutBps">Bytes a second sent.</param>
/// <param name="InTotalB">Bytes received since boot.</param>
/// <param name="OutTotalB">Bytes sent since boot.</param>
public sealed record BoxNet(string Name, double InBps, double OutBps, long InTotalB, long OutTotalB);

/// <summary>One file system, and the array under it when there is one.</summary>
/// <param name="Mount">Where it is mounted.</param>
/// <param name="Device">The device it is on.</param>
/// <param name="Fstype">Its type.</param>
/// <param name="SizeB">Its size.</param>
/// <param name="UsedB">What is used.</param>
/// <param name="RaidState">The array's state from /proc/mdstat, with "needs sudo" beside it when mdadm would not answer; null off an array.</param>
/// <param name="Members">How many members the array has.</param>
/// <param name="MembersActive">How many of them are up.</param>
public sealed record BoxDisk(string Mount, string Device, string Fstype, long SizeB, long UsedB, string? RaidState, int? Members, int? MembersActive);

/// <summary>One GPU, from nvidia-smi.</summary>
/// <param name="Index">Its number.</param>
/// <param name="Name">Its model.</param>
/// <param name="Load">Its busy share, 0 to 100.</param>
/// <param name="MemUsedB">Memory in use.</param>
/// <param name="MemTotalB">All of its memory.</param>
/// <param name="Temp">Its temperature.</param>
/// <param name="SlowdownTemp">Where it slows itself down, as the driver says.</param>
/// <param name="ShutdownTemp">Where it shuts itself off, as the driver says.</param>
/// <param name="PowerW">What it draws.</param>
/// <param name="PowerLimitW">What it may draw.</param>
/// <param name="Pstate">Its performance state, P0 to P12.</param>
public sealed record BoxGpu(int Index, string Name, double Load, long MemUsedB, long MemTotalB, double Temp, double? SlowdownTemp, double? ShutdownTemp, double? PowerW, double? PowerLimitW, string Pstate);

/// <summary>One temperature sensor with its own marks.</summary>
/// <param name="Sensor">The sensor's name and label.</param>
/// <param name="Now">What it reads.</param>
/// <param name="High">Its high mark, when it has one.</param>
/// <param name="Critical">Its critical mark, when it has one.</param>
public sealed record BoxTemp(string Sensor, double Now, double? High, double? Critical);

/// <summary>One service, from systemd.</summary>
/// <param name="Unit">The unit's name.</param>
/// <param name="Scope">"user" or "system".</param>
/// <param name="ActiveState">systemd's active state: active, inactive, failed.</param>
/// <param name="SubState">systemd's sub state: running, exited, waiting.</param>
/// <param name="Since">When it entered that state, when systemd says.</param>
/// <param name="Note">What else the agent read: a timer's next run, a service's last result.</param>
public sealed record BoxService(string Unit, string Scope, string ActiveState, string SubState, DateTimeOffset? Since, string? Note);

/// <summary>One live sample of the box, posted every two seconds: the cores, the memory, the network and the GPUs.</summary>
/// <param name="At">When it was read, by the box's clock.</param>
/// <param name="Cores">Each core's busy share, 0 to 100, in core order.</param>
/// <param name="MemoryUsedB">Memory programs hold.</param>
/// <param name="MemoryTotalB">All the memory.</param>
/// <param name="SwapUsedB">Swap in use.</param>
/// <param name="SwapTotalB">All the swap.</param>
/// <param name="Nets">Each interface's rates.</param>
/// <param name="Gpus">Each GPU's load.</param>
public sealed record BoxLiveSample(DateTimeOffset At, IReadOnlyList<double> Cores, long MemoryUsedB, long MemoryTotalB, long SwapUsedB, long SwapTotalB, IReadOnlyList<BoxNetRate> Nets, IReadOnlyList<BoxGpuLoad> Gpus);

/// <summary>One interface's rates in a live sample.</summary>
/// <param name="Name">The interface.</param>
/// <param name="InBps">Bytes a second received.</param>
/// <param name="OutBps">Bytes a second sent.</param>
public sealed record BoxNetRate(string Name, double InBps, double OutBps);

/// <summary>One GPU's load in a live sample.</summary>
/// <param name="Index">The GPU.</param>
/// <param name="Load">Its busy share, 0 to 100.</param>
public sealed record BoxGpuLoad(int Index, double Load);
// #endregion box-snapshot
