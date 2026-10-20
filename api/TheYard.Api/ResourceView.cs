// The parts every resource card's view builds the shared frame from: a dial from a reading and
// its limit, the sixty-minute sparklines, the chart with deploys and events marked, the uptime of
// the window, and the words a missing reading is printed as. Each card's own file then says only
// which reading is which and what its four questions are.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

// #region resource-view
/// <summary>The shared pieces of a resource card, from its inputs.</summary>
public static class ResourceView
{
    /// <summary>The newest kept value of a reading, or null.</summary>
    public static double? Now(ResourceCardInputs inputs, string reading) =>
        inputs.LastHour.LastOrDefault(minute => minute.Values.GetValueOrDefault(reading) is not null)?.Values[reading];

    /// <summary>A reading's newest sixty minutes, oldest first, for the sparkline and the sustained tone.</summary>
    public static IReadOnlyList<double?> Hour(ResourceCardInputs inputs, string reading) =>
        inputs.LastHour.Select(minute => minute.Values.GetValueOrDefault(reading)).ToList();

    /// <summary>A reading as a share of its limit, in percent.</summary>
    public static double? Share(double? value, double? limit) => value is { } v && limit is > 0 ? Math.Round(100 * v / limit.Value, 2) : null;

    /// <summary>
    /// A dial: the reading's share of its limit now, its thirty-day 95th and peak, and the tone the
    /// shared rule gives it; <paramref name="percentReading"/> is true when the reading is already a percentage.
    /// </summary>
    public static CloudDial Dial(ResourceCardInputs inputs, string reading, string name, double? limit, (double Amber, double Red) levels, Func<double?, string> absolute, bool percentReading = false, bool hourly = false)
    {
        double? now = Now(inputs, reading);
        double? share = percentReading ? now : Share(now, limit);
        var (p95, peak) = ResourceFolding.Spread(inputs.Month, reading);
        double? toShare(double? v) => percentReading ? v : Share(v, limit);
        var sustained = Tone(Hour(inputs, reading).Select(toShare).ToList(), levels, hourly);
        return new CloudDial(name, share, PlanView.Percent(share), absolute(now), levels.Amber, levels.Red, toShare(p95), toShare(peak), CloudTones.Dial(share, levels, sustained));
    }

    // #region hourly-tone
    /// <summary>
    /// The tone of a reading's newest values: held fifteen minutes for a minute's source, and the
    /// newest value alone for an hourly one, whose one document already stands for its hour.
    /// </summary>
    public static PlanTone Tone(IReadOnlyList<double?> values, (double Amber, double Red) levels, bool hourly)
    {
        if (!hourly)
        {
            return PlanThresholds.ToneOf(values, levels);
        }
        return values.LastOrDefault(v => v is not null) is not { } newest ? PlanTone.Info
            : newest >= levels.Red ? PlanTone.Alarm
            : newest >= levels.Amber ? PlanTone.Warning
            : PlanTone.Good;
    }
    // #endregion hourly-tone

    /// <summary>A reading's sixty minutes as a sparkline, scaled to its limit.</summary>
    public static CloudSpark Spark(ResourceCardInputs inputs, string reading, string name, double? limit, bool percentReading = false) =>
        new(name, Hour(inputs, reading).Select(v => percentReading ? v : Share(v, limit)).ToList());

    /// <summary>The window's chart of two readings as shares of their limits, with deploys and the hours' events marked.</summary>
    public static CloudChart Chart(ResourceCardInputs inputs, string title, (string Reading, string Name, double? Limit, bool Percent) first, (string Reading, string Name, double? Limit, bool Percent) second, double redLine, string legend, IEnumerable<CloudMark>? extra = null)
    {
        CloudSeries series((string Reading, string Name, double? Limit, bool Percent) s) =>
            new(s.Name, inputs.Buckets.Select(b => new CloudPoint(b.At.ToUnixTimeMilliseconds(), s.Percent ? b.Means.GetValueOrDefault(s.Reading) : Share(b.Means.GetValueOrDefault(s.Reading), s.Limit))).ToList());
        var marks = Deploys(inputs.Buckets)
            .Concat(inputs.Hours.SelectMany(h => h.Events).Where(e => e.Kind is "restart" or "maintenance" || !e.Planned)
                .Select(e => new CloudMark(e.At.ToUnixTimeMilliseconds(), "event", $"{e.Title} on {e.Resource}")))
            .Concat(extra ?? [])
            .OrderBy(mark => mark.AtMs)
            .ToList();
        return new CloudChart($"{title}, {CloudTones.WindowWords(inputs.Window)}", [series(first), series(second)], marks, redLine, legend, (inputs.Now - inputs.Length).ToUnixTimeMilliseconds());
    }

    /// <summary>A gold line where a bucket first shows a build the bucket before it did not.</summary>
    public static IEnumerable<CloudMark> Deploys(IReadOnlyList<ResourceBucket> buckets)
    {
        string? before = null;
        foreach (var bucket in buckets)
        {
            if (before is not null && bucket.Version is not null && bucket.Version != before)
            {
                yield return new CloudMark(bucket.At.ToUnixTimeMilliseconds(), "deploy", $"Deployed {bucket.Version}");
            }
            before = bucket.Version ?? before;
        }
    }

    /// <summary>
    /// The share of the window the resource answered: minutes kept against minutes since the first
    /// kept one, so a resource the card began reading yesterday is not counted as down before then.
    /// </summary>
    public static double? Uptime(ResourceCardInputs inputs, double minutesPerReading = 1)
    {
        if (inputs.Buckets.Count == 0)
        {
            return null;
        }
        double elapsed = (inputs.Now - inputs.Buckets[0].At).TotalMinutes;
        return elapsed <= 0 ? null : Math.Round(Math.Min(100, 100 * inputs.Buckets.Sum(b => b.Minutes) * minutesPerReading / elapsed), 2);
    }

    /// <summary>The newest hour's facts, or null before the first hour is read.</summary>
    public static ResourceHour? Latest(ResourceCardInputs inputs) => inputs.Hours.Count == 0 ? null : inputs.Hours[^1];

    /// <summary>A fact from the newest hour, or the words for one not read.</summary>
    public static string Fact(ResourceCardInputs inputs, string key, string missing = "not read") =>
        Latest(inputs)?.Facts.GetValueOrDefault(key) ?? missing;

    /// <summary>A number the way a readout prints it.</summary>
    public static string Number(double? value, string format = "N0") => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "not read";

    /// <summary>The resource's link in the header, from the newest hour's id and tenant; the edge's comes from Netlify.</summary>
    public static CloudLink? Resource(ResourceCardInputs inputs)
    {
        var hour = Latest(inputs);
        return hour is null ? null : new CloudLink(PlanReader.NameOf(hour.ResourceId), PlanView.Portal(hour.TenantId, hour.ResourceId));
    }

    /// <summary>The cost line, read from the bill's kept days.</summary>
    public static CloudLine Cost(ResourceCardInputs inputs) =>
        new("Cost", inputs.CostMonth is { } dollars ? $"${dollars.ToString("0.00", CultureInfo.InvariantCulture)} / mo" : "not on the bill yet", "info");

    /// <summary>The live-read line: how old the newest kept minute is.</summary>
    public static CloudLine LiveRead(ResourceCardInputs inputs, bool hourly = false)
    {
        long? age = inputs.LastHour.Count == 0 ? null : (long)(inputs.Now - inputs.LastHour[^1].At).TotalSeconds;
        // An hourly source's document is stamped at its hour, so it is late only past two of them.
        long late = hourly ? 7_200 : 300;
        return new("Live read", age is { } seconds ? $"{seconds} s ago" : "not read yet", age > late ? "warning" : "info");
    }

    /// <summary>How old the newest kept minute is, in seconds.</summary>
    public static long? Age(ResourceCardInputs inputs) => inputs.LastHour.Count == 0 ? null : (long)(inputs.Now - inputs.LastHour[^1].At).TotalSeconds;

    /// <summary>The note a card prints when its keep is missing or empty; null when it has minutes.</summary>
    public static string? Note(ResourceCardInputs inputs, string firstRead) =>
        !inputs.Availability.Available ? inputs.Availability.Reason
            : inputs.LastHour.Count == 0 ? firstRead
            : null;

    /// <summary>The empty-keep sentence, naming the recording site by its configured name.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="grain">What the card keeps: minute or hour.</param>
    public static string FirstRead(ResourceCardInputs inputs, string grain) =>
        $"no {grain} kept yet; the first is read a minute after {inputs.RecordingSite} starts";

    /// <summary>A sum of a reading over the newest sixty minutes.</summary>
    public static double? HourSum(ResourceCardInputs inputs, string reading)
    {
        var read = Hour(inputs, reading).Where(v => v is not null).Select(v => v!.Value).ToList();
        return read.Count == 0 ? null : Math.Round(read.Sum(), 2);
    }

    /// <summary>A sum of a reading over the window's buckets.</summary>
    public static double? WindowSum(ResourceCardInputs inputs, string reading)
    {
        var read = inputs.Buckets.Select(b => b.Sums.GetValueOrDefault(reading)).Where(v => v is not null).Select(v => v!.Value).ToList();
        return read.Count == 0 ? null : Math.Round(read.Sum(), 2);
    }
}
// #endregion resource-view
