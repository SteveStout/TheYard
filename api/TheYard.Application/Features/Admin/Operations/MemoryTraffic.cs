// Whether memory follows traffic: the processor should rise and fall with the requests, and memory
// should stay roughly where it is whatever they do. Memory that keeps climbing while the traffic is
// flat is a leak or a cache with no bound, the shape the catalogue held in each process once made
// (ADR: Memory follows traffic). Pure arithmetic over points, so a test can hold the rule
// to the minutes the sites really kept.

using TheYard.Application.Shared.History;

namespace TheYard.Application.Features.Admin.Operations;

/// <summary>One point of the window: the requests a minute, the processor and the memory at that time.</summary>
/// <param name="At">The start of the minute or bucket.</param>
/// <param name="RequestsPerMinute">The requests answered, per minute, averaged over the bucket.</param>
/// <param name="CpuPercent">The process's processor, in percent; null when not read.</param>
/// <param name="WorkingSetMb">The process's working set, in megabytes.</param>
public sealed record TrafficPoint(DateTimeOffset At, double RequestsPerMinute, double? CpuPercent, double WorkingSetMb);

/// <summary>What the window says about memory against traffic, in numbers and in one sentence.</summary>
/// <param name="Tone">Red when memory climbed while traffic stayed flat; green when it stayed put or followed traffic; black with too few points.</param>
/// <param name="Words">The finding in one sentence.</param>
/// <param name="MemoryFromMb">The middle working set of the window's first half.</param>
/// <param name="MemoryToMb">The middle working set of the window's second half.</param>
/// <param name="RequestsFrom">The middle requests a minute of the first half.</param>
/// <param name="RequestsTo">The middle requests a minute of the second half.</param>
/// <param name="ClimbThresholdMb">How far memory must rise to count as climbing: 50 MB or 5 percent of the limit, whichever is more.</param>
/// <param name="CpuFollowsTraffic">How closely the processor moved with the requests, from -1 to 1; null when either did not move.</param>
/// <param name="MemoryFollowsTraffic">How closely the memory moved with the requests, from -1 to 1; null when either did not move.</param>
public sealed record MemoryTrafficVerdict(
    PlanTone Tone,
    string Words,
    double MemoryFromMb,
    double MemoryToMb,
    double RequestsFrom,
    double RequestsTo,
    double ClimbThresholdMb,
    double? CpuFollowsTraffic,
    double? MemoryFollowsTraffic);

/// <summary>The rule, and the arithmetic it stands on.</summary>
public static class MemoryTraffic
{
    /// <summary>The fewest points a window needs before it is judged.</summary>
    public const int FewestPoints = 6;

    /// <summary>Traffic counts as flat when the second half's middle is no more than this much above the first half's.</summary>
    public const double FlatTrafficRise = 0.25;

    // #region rule
    /// <summary>
    /// The window split in two by time. Memory is climbing when the second half's middle working
    /// set is at least the threshold above the first half's and the line through every point
    /// slopes up; traffic is flat when the second half's middle requests a minute is at most a
    /// quarter above the first half's, plus one request. Both at once is red. Middles rather than
    /// averages, so one busy minute or one collection does not decide the window.
    /// </summary>
    public static MemoryTrafficVerdict Judge(IReadOnlyList<TrafficPoint> points, double memoryLimitMb)
    {
        double threshold = Math.Max(50, memoryLimitMb * 0.05);
        var ordered = points.OrderBy(point => point.At).ToList();
        if (ordered.Count < FewestPoints)
        {
            return new MemoryTrafficVerdict(PlanTone.Info, $"Not judged yet: {ordered.Count} of the {FewestPoints} readings a window needs.", 0, 0, 0, 0, threshold, null, null);
        }

        var first = ordered.Take(ordered.Count / 2).ToList();
        var second = ordered.Skip(ordered.Count / 2).ToList();
        double memoryFrom = Middle(first.Select(point => point.WorkingSetMb));
        double memoryTo = Middle(second.Select(point => point.WorkingSetMb));
        double requestsFrom = Middle(first.Select(point => point.RequestsPerMinute));
        double requestsTo = Middle(second.Select(point => point.RequestsPerMinute));
        bool climbing = memoryTo - memoryFrom >= threshold && Slope(ordered) > 0;
        bool flat = requestsTo <= requestsFrom * (1 + FlatTrafficRise) + 1;

        var withCpu = ordered.Where(point => point.CpuPercent is not null).ToList();
        double? cpu = Correlation(withCpu.Select(point => point.RequestsPerMinute).ToList(), withCpu.Select(point => point.CpuPercent!.Value).ToList());
        double? memory = Correlation(ordered.Select(point => point.RequestsPerMinute).ToList(), ordered.Select(point => point.WorkingSetMb).ToList());

        string from = $"{memoryFrom:N0} MB", to = $"{memoryTo:N0} MB";
        string traffic = $"{requestsFrom:N1} to {requestsTo:N1} requests a minute";
        var (tone, words) = (climbing, flat) switch
        {
            (true, true) => (PlanTone.Alarm, $"Memory climbed from {from} to {to} while traffic stayed flat ({traffic}): a leak or a cache with no bound."),
            (true, false) => (PlanTone.Good, $"Memory rose from {from} to {to} as traffic rose ({traffic}), and is worth watching if it does not come down with it."),
            _ => (PlanTone.Good, $"Memory held at about {to} ({from} in the first half) while traffic went {traffic}."),
        };
        return new MemoryTrafficVerdict(tone, words, Math.Round(memoryFrom, 1), Math.Round(memoryTo, 1), Math.Round(requestsFrom, 2), Math.Round(requestsTo, 2), threshold, Round(cpu), Round(memory));
    }
    // #endregion rule

    private static double Middle(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }
        int half = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[half] : (sorted[half - 1] + sorted[half]) / 2;
    }

    /// <summary>The least-squares slope of the working set against the point's place in the window.</summary>
    private static double Slope(IReadOnlyList<TrafficPoint> ordered)
    {
        double n = ordered.Count;
        double meanX = (n - 1) / 2;
        double meanY = ordered.Average(point => point.WorkingSetMb);
        double top = 0, bottom = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            top += (i - meanX) * (ordered[i].WorkingSetMb - meanY);
            bottom += (i - meanX) * (i - meanX);
        }
        return bottom == 0 ? 0 : top / bottom;
    }

    /// <summary>Pearson's correlation of two equal lists, or null when there are too few or either did not move.</summary>
    private static double? Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count < 3 || x.Count != y.Count)
        {
            return null;
        }
        double meanX = x.Average(), meanY = y.Average();
        double top = 0, left = 0, right = 0;
        for (int i = 0; i < x.Count; i++)
        {
            top += (x[i] - meanX) * (y[i] - meanY);
            left += (x[i] - meanX) * (x[i] - meanX);
            right += (y[i] - meanY) * (y[i] - meanY);
        }
        return left == 0 || right == 0 ? null : top / Math.Sqrt(left * right);
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 2) : null;
}
