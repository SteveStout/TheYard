using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TheYard.Application;

namespace TheYard.Api;

// Site activity: the graph at the top of the Admin tab and the table behind
// the operator's key (ADR: Site activity, and the line an address does not
// cross). Three pieces: how a request becomes a hit with nothing in it a
// person could be named by, the collector that takes hits off the request
// path and writes them in batches to the store that served them, and the
// report the two endpoints serve.

// #region visitor-token
/// <summary>
/// The address, made into something the page can group by and nobody can
/// reverse. A keyed hash of the day and the address: keyed with the signing
/// key, which every container shares and nobody outside has, so the same
/// address is the same token on both sites within a day and a different one
/// tomorrow; the day is in the hash rather than in a salt table, so there is
/// no table of salts to keep or to lose. The address itself is kept only as
/// its first three octets, which is enough to see a network, a country and an
/// obvious scanner range, and not enough to name a machine. A full address is
/// never stored, and a full address is never sent anywhere from here.
/// </summary>
public sealed class VisitorTokens(string key)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(key);

    /// <summary>Thirty-two hex characters: the first sixteen bytes of the keyed hash.</summary>
    public string TokenFor(string address, DateTimeOffset at)
    {
        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ActivityFolding.DayOf(at) + "|" + address));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>203.0.113.7 becomes 203.0.113.x; an IPv6 address keeps its first three groups and ends in x; anything unreadable is x.</summary>
    public static string NetworkOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "x";
        }

        string[] octets = address.Split('.');
        if (octets.Length == 4 && octets.All(octet => int.TryParse(octet, out int value) && value is >= 0 and <= 255))
        {
            return $"{octets[0]}.{octets[1]}.{octets[2]}.x";
        }

        if (address.Contains(':', StringComparison.Ordinal))
        {
            string[] groups = address.Split(':');
            return string.Join(':', groups.Take(3)) + ":x";
        }

        return "x";
    }

    /// <summary>
    /// The visitor's address as the edge reports it. Netlify writes the
    /// connecting client's address into its own header and the standard one;
    /// a request that reaches the origin without either is a direct one and
    /// the connection says who. A header is what the sender says it is, which
    /// is fine for a count of visitors and would not be fine for anything that
    /// grants something.
    /// </summary>
    public static string AddressOf(HttpContext context)
    {
        string? netlify = context.Request.Headers["X-Nf-Client-Connection-Ip"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(netlify))
        {
            return netlify.Trim();
        }

        string? forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "";
    }
}
// #endregion visitor-token

// #region hits
/// <summary>
/// What is recorded about a request, and what is not. The path, cut at the
/// query string and with any at sign encoded, so an address pasted into a
/// URL cannot travel; the store that served it; a guess at whether it was a
/// person. Photos and the page's own assets are not recorded, because one
/// visit is one page and not the forty files it is made of, and the Admin
/// tab's own reads are excluded by the caller for the reason the request ring
/// gives.
/// </summary>
public static class Hits
{
    private static readonly Regex BotAgent = new(
        @"bot|crawl|spider|slurp|curl|wget|python|httpclient|go-http|java/|libwww|scan|nmap|masscan|zgrab|censys|nuclei|headless|phantom",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BotPath = new(
        @"\.php$|/wp-|/\.env|/\.git|/xmlrpc|/phpmyadmin|/cgi-bin|/vendor/|/\.aws|/config\.|/\.well-known/security",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> Assets = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".webp", ".gif",
        ".woff", ".woff2", ".ttf", ".txt", ".xml", ".json", ".webmanifest",
    };

    /// <summary>Is this request one the activity feature counts.</summary>
    public static bool Counts(string path)
    {
        if (path.StartsWith("/api/images/", StringComparison.Ordinal))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Length == 0 || !Assets.Contains(extension);
    }

    public static bool LooksLikeABot(string? userAgent, string path) =>
        string.IsNullOrWhiteSpace(userAgent) || BotAgent.IsMatch(userAgent) || BotPath.IsMatch(path);

    /// <summary>The path a row keeps: no query string, bounded, and with no at sign in it.</summary>
    public static string PathOf(string path)
    {
        string kept = path.Length > 200 ? path[..200] : path;
        return kept.Replace("@", "%40", StringComparison.Ordinal);
    }
}
// #endregion hits

// #region collector
/// <summary>
/// Hits off the request path. A request offers its hit to a bounded channel
/// and goes on its way; this service drains the channel every few seconds,
/// or sooner when it fills, and hands each store the hits it served. A full
/// channel drops the oldest hit rather than blocking a request, because a
/// count of visitors is never worth a visitor's time, and the drop is
/// counted so the card can say it happened.
/// </summary>
public sealed class ActivityCollector : BackgroundService
{
    public const int Capacity = 10_000;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int DrainAt = 500;

    private readonly Channel<ActivityHit> _channel = Channel.CreateBounded<ActivityHit>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly IReadOnlyDictionary<string, IActivityStore> _stores;
    private readonly ILogger<ActivityCollector> _logger;
    private int _pending;
    private long _offered;
    private long _written;
    private long _failedBatches;
    private DateTimeOffset? _lastWrite;

    public ActivityCollector(IReadOnlyDictionary<string, IActivityStore> stores, ILogger<ActivityCollector> logger)
    {
        _stores = stores;
        _logger = logger;
    }

    /// <summary>The stores by key, for the report.</summary>
    public IReadOnlyDictionary<string, IActivityStore> Stores => _stores;

    /// <summary>What has passed through: offered, written, batches that failed, and the last time anything was written.</summary>
    public (long Offered, long Written, long FailedBatches, DateTimeOffset? LastWrite) Counters =>
        (Interlocked.Read(ref _offered), Interlocked.Read(ref _written), Interlocked.Read(ref _failedBatches), _lastWrite);

    /// <summary>Called on the request thread and returns at once.</summary>
    public void Offer(ActivityHit hit)
    {
        if (_channel.Writer.TryWrite(hit))
        {
            Interlocked.Increment(ref _offered);
            if (Interlocked.Increment(ref _pending) >= DrainAt)
            {
                _wake.TrySetResult();
            }
        }
    }

    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAny(Task.Delay(Interval, stopping), _wake.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await DrainAsync(CancellationToken.None);
        }

        // Whatever is left goes out with the process.
        await DrainAsync(CancellationToken.None);
    }

    /// <summary>One pass: everything queued right now, grouped by the store that served it, one batch per store.</summary>
    public async Task DrainAsync(CancellationToken cancellation)
    {
        var batch = new List<ActivityHit>();
        while (_channel.Reader.TryRead(out var hit))
        {
            batch.Add(hit);
        }

        Interlocked.Exchange(ref _pending, 0);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var group in batch.GroupBy(hit => hit.Store, StringComparer.Ordinal))
        {
            if (!_stores.TryGetValue(group.Key, out var store))
            {
                continue;
            }

            var hits = group.ToList();
            try
            {
                await store.RecordAsync(hits, cancellation);
                Interlocked.Add(ref _written, hits.Count);
                _lastWrite = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedBatches);
                // The type, never the message: a store's message can name a server.
                _logger.LogWarning("An activity batch of {Count} hits for {Store} was dropped: {Type}", hits.Count, group.Key, ex.GetType().Name);
            }
        }
    }
}
// #endregion collector

// #region report
/// <summary>The windows the graph offers, and the bucket each one is drawn in.</summary>
public static class ActivityWindows
{
    public static (TimeSpan Length, TimeSpan Bucket, string Name)? Parse(string? window) => (window ?? "24h") switch
    {
        "24h" => (TimeSpan.FromHours(24), TimeSpan.FromHours(1), "24h"),
        "7d" => (TimeSpan.FromDays(7), TimeSpan.FromHours(6), "7d"),
        "30d" => (TimeSpan.FromDays(30), TimeSpan.FromDays(1), "30d"),
        _ => null,
    };
}

/// <summary>
/// The report both endpoints are built from. The public one carries the
/// series, the totals and the top paths and names nobody; the keyed one
/// carries the visitor rows. Both are read from every store this container
/// runs, so the graph shows the two stores against each other from one
/// container's point of view, which is the point of writing the row where
/// the request was served.
/// </summary>
public static class ActivityReport
{
    public static async Task<object> PublicAsync(ActivityCollector collector, Backends backends, string window, DateTimeOffset now, CancellationToken cancellation)
    {
        var chosen = ActivityWindows.Parse(window)!.Value;
        DateTimeOffset since = ActivityFolding.HourOf(now - chosen.Length);
        var stores = new List<object>();
        var series = new List<object>();
        var paths = new Dictionary<string, int>(StringComparer.Ordinal);
        int requests = 0;
        int bots = 0;
        var byStore = new List<object>();

        foreach (var backend in backends.All)
        {
            var store = collector.Stores.GetValueOrDefault(backend.Key) ?? NullActivityStore.Instance;
            var availability = await store.AvailabilityAsync(cancellation);
            stores.Add(new { store = backend.Key, name = backend.Name, available = availability.Available, reason = availability.Reason });

            IReadOnlyList<ActivityHour> hours = availability.Available ? await store.HoursAsync(since, cancellation) : [];
            var points = Bucket(hours.Where(hour => hour.Store == backend.Key), since, now, chosen.Bucket);
            series.Add(new { store = backend.Key, name = backend.Name, points });

            int storeRequests = hours.Sum(hour => hour.Requests);
            int storeBots = hours.Sum(hour => hour.Bots);
            requests += storeRequests;
            bots += storeBots;
            byStore.Add(new { store = backend.Key, requests = storeRequests, bots = storeBots, humans = storeRequests - storeBots });
            foreach (var hour in hours)
            {
                foreach (var (path, count) in hour.Paths)
                {
                    paths[path] = paths.GetValueOrDefault(path) + count;
                }
            }
        }

        var counters = collector.Counters;
        return new
        {
            window = chosen.Name,
            bucket = chosen.Bucket == TimeSpan.FromDays(1) ? "day" : chosen.Bucket == TimeSpan.FromHours(6) ? "six hours" : "hour",
            since,
            until = now,
            totals = new { requests, bots, humans = requests - bots },
            by_store = byStore,
            series,
            top_paths = paths.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(12)
                .Select(entry => new { path = entry.Key, requests = entry.Value }).ToList(),
            stores,
            collector = new
            {
                offered = counters.Offered,
                written = counters.Written,
                failed_batches = counters.FailedBatches,
                last_write = counters.LastWrite,
                interval_seconds = (int)ActivityCollector.Interval.TotalSeconds,
            },
        };
    }

    public static async Task<object> VisitorsAsync(ActivityCollector collector, Backends backends, string window, DateTimeOffset now, CancellationToken cancellation)
    {
        var chosen = ActivityWindows.Parse(window)!.Value;
        DateTimeOffset since = now - chosen.Length;
        var rows = new List<object>();
        foreach (var backend in backends.All)
        {
            var store = collector.Stores.GetValueOrDefault(backend.Key) ?? NullActivityStore.Instance;
            foreach (var visitor in await store.VisitorsAsync(since, cancellation))
            {
                if (visitor.LastSeen < since)
                {
                    continue;
                }

                rows.Add(new
                {
                    visitor = visitor.Visitor,
                    network = visitor.Network,
                    store = visitor.Store,
                    day = visitor.Day,
                    first_seen = visitor.FirstSeen,
                    last_seen = visitor.LastSeen,
                    requests = visitor.Requests,
                    bots = visitor.Bots,
                    top_paths = visitor.Paths.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(5)
                        .Select(entry => new { path = entry.Key, requests = entry.Value }).ToList(),
                });
            }
        }

        return new
        {
            window = chosen.Name,
            since,
            until = now,
            count = rows.Count,
            // Newest first, and no more than five hundred: a table longer than
            // that is a file, not a page.
            visitors = rows.Take(500).ToList(),
        };
    }

    /// <summary>The hours of one store laid onto a fixed grid of buckets from since to now, zeros where nothing happened, so the two lines share an x axis.</summary>
    private static List<object> Bucket(IEnumerable<ActivityHour> hours, DateTimeOffset since, DateTimeOffset now, TimeSpan bucket)
    {
        var start = bucket == TimeSpan.FromDays(1)
            ? new DateTimeOffset(since.Year, since.Month, since.Day, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(since.Year, since.Month, since.Day, since.Hour - (since.Hour % (int)Math.Max(1, bucket.TotalHours)), 0, 0, TimeSpan.Zero);
        var counts = new SortedDictionary<DateTimeOffset, (int Requests, int Bots)>();
        for (var at = start; at <= now; at += bucket)
        {
            counts[at] = (0, 0);
        }

        foreach (var hour in hours)
        {
            long index = (long)Math.Floor((hour.Hour - start) / bucket);
            if (index < 0)
            {
                continue;
            }

            var at = start + (bucket * index);
            var (requests, bots) = counts.GetValueOrDefault(at);
            counts[at] = (requests + hour.Requests, bots + hour.Bots);
        }

        return counts.Select(entry => (object)new { at = entry.Key, requests = entry.Value.Requests, bots = entry.Value.Bots }).ToList();
    }
}

/// <summary>
/// The key the visitor rows sit behind. Configuration, filled at roll time
/// like the signing key; unset means the endpoint does not exist, which is
/// the default and the safe one. Compared in constant time, and only ever
/// against the whole value.
/// </summary>
public sealed class AdminKey(string? configured)
{
    private readonly byte[]? _key = string.IsNullOrWhiteSpace(configured) || configured.StartsWith("__", StringComparison.Ordinal)
        ? null
        : Encoding.UTF8.GetBytes(configured.Trim());

    public bool Configured => _key is not null;

    public bool Admits(string? presented)
    {
        if (_key is null || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(presented);
        return bytes.Length == _key.Length && CryptographicOperations.FixedTimeEquals(bytes, _key);
    }
}
// #endregion report
