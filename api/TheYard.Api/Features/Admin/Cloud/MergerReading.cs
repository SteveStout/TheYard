// The tracker merger's health for the nightly card (ADR: The tracker merger runs in Azure): five
// numbers the job hunt's Function answers at /api/merger/health, read once a minute beside the
// Function's metrics and kept on the same minute. The site holds no key to the share and gets none;
// the Function lists its two spool folders and says how many files wait and how long, and this
// reader keeps only those numbers, so no file name, row or log line can reach the site.
using System.Globalization;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>Reads the merger's health from the Function and turns it into the minute's readings.</summary>
public static class MergerReading
{
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(10));

    /// <summary>The readings a minute keeps, by the Function's own names with a prefix of their own.</summary>
    public const string LogAge = "merger_log_age_s", Waiting = "merger_waiting", Over5 = "merger_waiting_over_5", Over10 = "merger_waiting_over_10", Oldest = "merger_oldest_wait_s";

    /// <summary>The Function's answer's names, each against the reading it is kept under. Only these are read.</summary>
    public static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["log_age_s"] = LogAge,
        ["waiting"] = Waiting,
        ["waiting_over_5"] = Over5,
        ["waiting_over_10"] = Over10,
        ["oldest_wait_s"] = Oldest,
    };

    /// <summary>The route on the Function's own host name, as Resource Manager gives it (properties.defaultHostName).</summary>
    /// <param name="host">The Function app's default host name.</param>
    public static Uri Address(string host) => new($"https://{host}/api/merger/health");

    /// <summary>
    /// The five readings from the Function's answer: a number under each known name, null for a
    /// name it left out or sent as anything but a number, and every other name dropped unread.
    /// </summary>
    /// <param name="answer">The Function's JSON.</param>
    public static IReadOnlyDictionary<string, double?> Readings(JsonElement answer) =>
        Fields.ToDictionary(
            field => field.Value,
            field => answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty(field.Key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
                ? number
                : (double?)null,
            StringComparer.Ordinal);

    /// <summary>The five readings, or null when the Function did not answer with a JSON object; never throws but for cancellation.</summary>
    /// <param name="host">The Function app's default host name.</param>
    /// <param name="cancellation">Stops the read.</param>
    public static async Task<IReadOnlyDictionary<string, double?>?> ReadAsync(string host, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Address(host));
            request.Headers.UserAgent.ParseAdd("TheYard-SelfRead/1 (nightly card)");
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return answer.RootElement.ValueKind == JsonValueKind.Object ? Readings(answer.RootElement) : null;
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // A Function that is cold, redeploying or gone costs this minute's merger line, never the Function's metrics.
            return null;
        }
    }

    /// <summary>
    /// The minutes with the merger's readings on the newest, or on a minute of their own at the read's
    /// end when the metrics answered none; the minutes as they were when there is no reading.
    /// </summary>
    /// <param name="minutes">The Function's metric minutes, oldest first.</param>
    /// <param name="merger">The five readings, or null.</param>
    /// <param name="to">The read's end.</param>
    /// <param name="version">The build that read them.</param>
    public static IReadOnlyList<ResourceMinute> Fold(IReadOnlyList<ResourceMinute> minutes, IReadOnlyDictionary<string, double?>? merger, DateTimeOffset to, string version)
    {
        if (merger is null)
        {
            return minutes;
        }
        if (minutes.Count == 0)
        {
            var utc = to.ToUniversalTime();
            return [new(new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero), merger, version)];
        }
        var newest = minutes[^1];
        var values = new Dictionary<string, double?>(newest.Values, StringComparer.Ordinal);
        foreach (var (key, value) in merger)
        {
            values[key] = value;
        }
        return [.. minutes.Take(minutes.Count - 1), newest with { Values = values }];
    }

    /// <summary>The newest kept minute that carries the merger's readings; null when the hour kept none.</summary>
    /// <param name="lastHour">The newest sixty kept minutes, oldest first.</param>
    public static ResourceMinute? Newest(IReadOnlyList<ResourceMinute> lastHour) =>
        lastHour.LastOrDefault(minute => minute.Values.GetValueOrDefault(Waiting) is not null);

    /// <summary>A number of seconds as the card prints an age: seconds under a minute, else minutes, else hours and minutes.</summary>
    /// <param name="seconds">The age.</param>
    public static string Age(double seconds)
    {
        long s = (long)Math.Max(0, seconds);
        return s < 60 ? $"{s} s"
            : s < 3600 ? $"{s / 60} min"
            : $"{s / 3600} h {s % 3600 / 60} min";
    }

    /// <summary>A reading as a whole number of files.</summary>
    /// <param name="value">The reading.</param>
    public static int Count(double? value) => (int)Math.Round(value ?? 0, MidpointRounding.AwayFromZero);

    /// <summary>The words for a count of files, one or many.</summary>
    /// <param name="count">How many.</param>
    public static string Files(int count) => count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " file" : " files");
}
