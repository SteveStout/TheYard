// The report the two activity endpoints serve: the public one with the series,
// the totals and the top paths, and the keyed one with the visitor rows. Its
// own file because it is the shape of what leaves the server; who a visitor
// was is decided in ActivityWho.cs, and Activity.cs lists the other parts.
using TheYard.Application;

namespace TheYard.Api;

// #region report
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
    /// <summary>
    /// The public report for a window: the series per store on a shared grid,
    /// the totals, unique visitors per day, who the traffic was, the top
    /// paths, which store keeps the rows and how long, what the collector has
    /// done, and what the feature has cost the keeper. It names nobody: the
    /// visitor rows are read only to be counted.
    /// </summary>
    public static async Task<object> PublicAsync(ActivityCollector collector, Backends backends, string window, DateTimeOffset now, bool visitorRows, CancellationToken cancellation)
    {
        var chosen = ActivityWindows.Parse(window)!.Value;
        DateTimeOffset since = (now - chosen.Length).UtcHour;
        var stores = new List<object>();
        var series = new List<object>();
        var paths = new Dictionary<string, int>(StringComparer.Ordinal);
        int requests = 0;
        int bots = 0;
        var byStore = new List<object>();
        var keptRows = new List<ActivityVisitor>();

        // One read, from the keeper: every store's rows live there, each one
        // naming the store that served it, so the two lines still show against
        // each other and a paused relational database cannot take the card
        // down with it.
        var keeper = collector.Keeper;
        var availability = await keeper.AvailabilityAsync(cancellation);
        IReadOnlyList<ActivityHour> kept = availability.Available ? await keeper.HoursAsync(since, cancellation) : [];
        // The visitor rows are read here for counts and thrown away: how many
        // distinct tokens each day saw, of which kind, and what each kind
        // asked for. The rows themselves leave the server only through the
        // keyed endpoint below.
        IReadOnlyList<ActivityVisitor> keptVisitors = availability.Available ? await keeper.VisitorsAsync(since, cancellation) : [];

        foreach (var backend in backends.All)
        {
            stores.Add(new { store = backend.Key, name = backend.Name, available = availability.Available, reason = availability.Reason, kept_by = collector.KeeperKey });

            var hours = kept.Where(hour => hour.Store == backend.Key).ToList();
            var points = Bucket(hours, since, now, chosen.Bucket);
            series.Add(new { store = backend.Key, name = backend.Name, points });

            keptRows.AddRange(keptVisitors.Where(visitor => visitor.Store == backend.Key && visitor.LastSeen >= since));

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
        // What the feature has cost the keeper since this process started, when
        // the keeper counts it (the document store does, in request units);
        // null on a store that has no such unit.
        var cost = (collector.Keeper as IActivityCost)?.Cost;
        return new
        {
            window = chosen.Name,
            // Whether the per-visitor rows are served on this site at all, so
            // the page knows whether to offer them (off by default).
            visitor_rows = visitorRows,
            bucket = chosen.Bucket == TimeSpan.FromDays(1) ? "day" : chosen.Bucket == TimeSpan.FromHours(6) ? "six hours" : "hour",
            since,
            until = now,
            totals = new { requests, bots, humans = requests - bots },
            by_store = byStore,
            series,
            days = Days(keptRows, backends.All.Select(backend => backend.Key).ToList(), since, now),
            // Who the traffic was, from the same visitor rows the days are:
            // people, scanners and crawlers, and the site reading itself, so
            // the card can show visitors only or everything.
            who = ActivityWho.Summary(keptRows, backends.All.Select(backend => backend.Key).ToList()),
            top_paths = paths.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(12)
                .Select(entry => new { path = entry.Key, requests = entry.Value }).ToList(),
            stores,
            kept_by = collector.KeeperKey,
            // The keeper's own sentence for how long the rows are kept ("kept in
            // Azure Cosmos DB with no expiry"), its own field on the port rather
            // than the reason a store is down.
            retention = availability.Available ? availability.Retention : null,
            collector = new
            {
                offered = counters.Offered,
                written = counters.Written,
                failed_batches = counters.FailedBatches,
                dropped = counters.Dropped,
                last_write = counters.LastWrite,
                interval_seconds = (int)ActivityCollector.Interval.TotalSeconds,
            },
            cost = cost is null ? null : new { request_units = Math.Round(cost.RequestUnits, 2), operations = cost.Operations, failures = cost.Failures },
        };
    }

    /// <summary>
    /// The keyed report for a window: one row per visitor token and day, with
    /// its network, store, first and last request, counts and top five paths,
    /// for the stores this container runs. Served only behind the operator's
    /// key (AdminKey), and capped at five hundred rows.
    /// </summary>
    public static async Task<object> VisitorsAsync(ActivityCollector collector, Backends backends, string window, DateTimeOffset now, CancellationToken cancellation)
    {
        var chosen = ActivityWindows.Parse(window)!.Value;
        DateTimeOffset since = now - chosen.Length;
        var rows = new List<object>();
        var known = backends.All.Select(backend => backend.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var visitor in await collector.Keeper.VisitorsAsync(since, cancellation))
        {
            if (visitor.LastSeen < since || !known.Contains(visitor.Store))
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

    /// <summary>
    /// Unique visitors per UTC day, the graph's series: every unique address
    /// per day, as the owner asked for it. One row per day in the window,
    /// zeros where nobody came: the distinct tokens that day across every
    /// store, the distinct tokens per store, and how many of them looked like
    /// people. A token is one address for one day, so "unique visitors" here
    /// is unique addresses, counted without keeping one. Each day also says
    /// who: people, scanners and crawlers, and the site's own reads, the three
    /// adding up to the day's visitor-days (ActivityWho has the rule).
    /// </summary>
    private static List<object> Days(List<ActivityVisitor> rows, IReadOnlyList<string> stores, DateTimeOffset since, DateTimeOffset now)
    {
        var days = new List<object>();
        var first = new DateTimeOffset(since.Year, since.Month, since.Day, 0, 0, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        for (var at = first; at <= last; at = at.AddDays(1))
        {
            string day = at.UtcDay;
            var today = rows.Where(row => row.Day == day).ToList();
            var tokens = today.Select(row => row.Visitor).Distinct(StringComparer.Ordinal).ToList();
            // A visitor is a person if any store saw a request of theirs that did not look like a bot.
            int humans = tokens.Count(token => today.Any(row => row.Visitor == token && row.Bots < row.Requests));
            var who = ActivityWho.Count(today);
            days.Add(new
            {
                day,
                visitors = tokens.Count,
                humans,
                bots = tokens.Count - humans,
                people = who.People,
                scanners = who.Scanners,
                self = who.Self,
                by_store = stores.Select(store =>
                {
                    var theirs = today.Where(row => row.Store == store).ToList();
                    var split = ActivityWho.Count(theirs);
                    return new
                    {
                        store,
                        visitors = theirs.Select(row => row.Visitor).Distinct(StringComparer.Ordinal).Count(),
                        people = split.People,
                        scanners = split.Scanners,
                        self = split.Self,
                    };
                }).ToList(),
            });
        }

        return days;
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
// #endregion report
