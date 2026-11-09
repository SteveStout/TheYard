// The plan card's live source (ADR: Live dials where the source allows, the addendum on the plan's /proc fork): this
// instance's memory and processor read from its own /proc every two seconds while the card is open. Memory is what the
// kernel says is not available of what it has; the processor is the busy share of every core since the last read.
// The fork's 30-minute compare against Azure Monitor's minutes for the plan failed on the processor, so the line says
// it is this container's own view; the dials keep the plan's minutes.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

// #region proc-live-source
/// <summary>This instance's memory and processor from /proc, every two seconds.</summary>
/// <param name="root">Where /proc is: "/proc" in production, a fixture folder in a test.</param>
public sealed class ProcLiveSource(string root = "/proc") : TheYard.Application.Shared.Live.ILiveSource
{
    private (long Busy, long Total)? _last;

    /// <inheritdoc />
    public string Key => "plan";

    /// <inheritdoc />
    public int GrainSeconds => 2;

    /// <inheritdoc />
    public string Source => "this container's own /proc, 2 s, not the plan's minute";

    /// <inheritdoc />
    public string? Absent => File.Exists(Path.Combine(root, "meminfo")) ? null : "this process runs where there is no /proc to read";

    /// <inheritdoc />
    public async Task<TheYard.Application.Shared.Live.LiveSample?> SampleAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        var memory = Memory(await File.ReadAllTextAsync(Path.Combine(root, "meminfo"), cancellation));
        var times = CpuTimes(await File.ReadAllTextAsync(Path.Combine(root, "stat"), cancellation));
        double? cpu = _last is { } last && times.Total > last.Total ? Math.Round(100.0 * (times.Busy - last.Busy) / (times.Total - last.Total), 1) : null;
        _last = times;
        return new TheYard.Application.Shared.Live.LiveSample(now, new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["memory"] = memory.Total > 0 ? Math.Round(100.0 * (memory.Total - memory.Available) / memory.Total, 1) : null,
            ["cpu"] = cpu,
            ["memory_gb"] = memory.Total > 0 ? Math.Round(memory.Total / 1024.0 / 1024.0, 2) : null,
        });
    }

    /// <summary>MemTotal and MemAvailable from /proc/meminfo, in kB.</summary>
    public static (long Total, long Available) Memory(string meminfo)
    {
        long Field(string name) => meminfo.Split('\n')
            .Where(line => line.StartsWith(name + ":", StringComparison.Ordinal))
            .Select(line => long.TryParse(line[(name.Length + 1)..].Trim().Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb) ? kb : 0)
            .FirstOrDefault();
        return (Field("MemTotal"), Field("MemAvailable"));
    }

    /// <summary>The busy and total jiffies of the aggregate cpu line of /proc/stat: idle and iowait are the idle part.</summary>
    public static (long Busy, long Total) CpuTimes(string stat)
    {
        var line = stat.Split('\n').FirstOrDefault(row => row.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return (0, 0);
        }
        var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(field => long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out long jiffies) ? jiffies : 0).ToList();
        long idle = values.ElementAtOrDefault(3) + values.ElementAtOrDefault(4);
        long total = values.Sum();
        return (total - idle, total);
    }
}
// #endregion proc-live-source
