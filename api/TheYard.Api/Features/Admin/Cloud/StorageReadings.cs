// The two pieces the storage cards share apart from their views: an hourly series kept as one fact (a
// start, a step and the values), and the blob service's own listing of a container, which is the one
// read that goes to the storage account and not to Azure Resource Manager. Its own file because both
// cards and the source read them.
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace TheYard.Api.Features.Admin.Cloud;

// #region storage-readings
/// <summary>An hourly series as one fact, so a card's hour document keeps thirty days of an Azure Monitor read in one line.</summary>
public static class Series
{
    /// <summary>The series as "start|v1,v2,...", the start in seconds since the epoch, one value an hour; empty for none.</summary>
    public static string Encode(IReadOnlyDictionary<DateTimeOffset, double> points)
    {
        if (points.Count == 0)
        {
            return "";
        }
        var start = points.Keys.Min();
        var end = points.Keys.Max();
        int hours = (int)(end - start).TotalHours + 1;
        var values = Enumerable.Range(0, hours).Select(i => points.TryGetValue(start.AddHours(i), out double v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : "");
        return $"{start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}|{string.Join(',', values)}";
    }

    /// <summary>The series a fact holds, oldest first; an hour Azure did not answer for is left out.</summary>
    public static IReadOnlyList<(DateTimeOffset At, double Value)> Decode(string? fact)
    {
        if (string.IsNullOrEmpty(fact) || fact.Split('|') is not [var start, var values] || !long.TryParse(start, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
        {
            return [];
        }
        var at = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return [.. values.Split(',').Select((v, i) => (At: at.AddHours(i), Text: v))
            .Where(p => double.TryParse(p.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(p => (p.At, double.Parse(p.Text, CultureInfo.InvariantCulture)))];
    }

    /// <summary>Hourly values summed into UTC days, oldest first.</summary>
    public static IReadOnlyList<(DateTimeOffset Day, double Value)> Daily(IReadOnlyList<(DateTimeOffset At, double Value)> hourly) =>
        [.. hourly.GroupBy(p => new DateTimeOffset(p.At.UtcDateTime.Date, TimeSpan.Zero)).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(p => p.Value)))];

    /// <summary>The value at a percentile of a list, nearest rank; null for none.</summary>
    public static double? Percentile(IEnumerable<double> values, double percent)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : sorted[Math.Clamp((int)Math.Ceiling(percent / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    /// <summary>An Azure Monitor answer split by one dimension: each value's hourly totals.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<DateTimeOffset, double>> Split(JsonElement answer)
    {
        var split = new Dictionary<string, IReadOnlyDictionary<DateTimeOffset, double>>(StringComparer.Ordinal);
        if (!answer.TryGetProperty("value", out var metrics))
        {
            return split;
        }
        foreach (var metric in metrics.EnumerateArray())
        {
            foreach (var timeseries in metric.GetProperty("timeseries").EnumerateArray())
            {
                string name = timeseries.TryGetProperty("metadatavalues", out var meta) && meta.GetArrayLength() > 0 ? meta[0].GetProperty("value").GetString() ?? "?" : "?";
                var points = new Dictionary<DateTimeOffset, double>();
                foreach (var point in timeseries.GetProperty("data").EnumerateArray())
                {
                    if (point.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number
                        && DateTimeOffset.TryParse(point.GetProperty("timeStamp").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                    {
                        points[at.ToUniversalTime()] = total.GetDouble();
                    }
                }
                split[name] = points;
            }
        }
        return split;
    }
}

/// <summary>One container as its listing read it: its blobs, their bytes, and the newest few.</summary>
/// <param name="Name">The container.</param>
/// <param name="Blobs">How many blobs it holds; null when the listing was refused.</param>
/// <param name="Bytes">Their bytes together.</param>
/// <param name="Newest">The newest blobs: name, bytes, last modified.</param>
/// <param name="Refused">The status the listing was refused with, or null.</param>
public sealed record ContainerListing(string Name, int? Blobs, long Bytes, IReadOnlyList<(string Name, long Bytes, DateTimeOffset At)> Newest, string? Refused);

/// <summary>The blob service's listing, read with the site's identity (Storage Blob Data Reader on the account).</summary>
/// <param name="clientId">The user-assigned identity's client id.</param>
/// <param name="configured">False off Azure, where there is no identity to ask with.</param>
/// <param name="identity">Where this process runs, which the token request needs.</param>
public sealed class BlobListing(string clientId, bool configured, HostIdentity? identity = null)
{
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>When the last listing was read, its status and how long it took: the card's "is it answering".</summary>
    public (DateTimeOffset At, int Status, long Ms)? Last { get; private set; }

    /// <summary>Each container's listing, up to five thousand blobs each; a refused listing says its status.</summary>
    public async Task<IReadOnlyList<ContainerListing>> ListAsync(string account, IReadOnlyList<string> containers, CancellationToken cancellation)
    {
        if (!configured || containers.Count == 0)
        {
            return [];
        }
        string token = await IdentityTokens.AcquireAsync(Http, "https://storage.azure.com/", clientId, identity ?? HostIdentity.None, cancellation);
        var listings = new List<ContainerListing>();
        foreach (string container in containers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{account}.blob.core.windows.net/{Uri.EscapeDataString(container)}?restype=container&comp=list&maxresults=5000");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("x-ms-version", "2023-11-03");
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var response = await Http.SendAsync(request, cancellation);
            Last = (DateTimeOffset.UtcNow, (int)response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                listings.Add(new ContainerListing(container, null, 0, [], ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)));
                continue;
            }
            listings.Add(Parse(container, await response.Content.ReadAsStringAsync(cancellation)));
        }
        return listings;
    }

    /// <summary>A listing's XML as the container's count, bytes and newest three blobs.</summary>
    public static ContainerListing Parse(string container, string xml)
    {
        var blobs = XDocument.Parse(xml).Descendants("Blob")
            .Select(b => (
                Name: (string?)b.Element("Name") ?? "?",
                Bytes: long.TryParse((string?)b.Element("Properties")?.Element("Content-Length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0,
                At: DateTimeOffset.TryParse((string?)b.Element("Properties")?.Element("Last-Modified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : DateTimeOffset.MinValue))
            .ToList();
        return new ContainerListing(container, blobs.Count, blobs.Sum(b => b.Bytes), [.. blobs.OrderByDescending(b => b.At).Take(3)], null);
    }
}
// #endregion storage-readings
