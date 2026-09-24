namespace TheYard.Application;

// #region activity-port
// Site activity, kept by the store that served the request (ADR: Site
// activity, and the line an address does not cross). One port, two adapters,
// the same shape as bids: the relational side keeps two tables, the document
// side keeps two kinds of document in one container, and the Admin tab reads
// either through this.
//
// What a hit carries is the whole privacy decision, so it is decided here and
// not in an adapter. Seven fields: when, a visitor token that is a keyed hash
// of the address and rotates daily, the address cut to its first three octets,
// the path with no query string, the store, whether the request looked like a
// bot, and on a page load the host of the page that linked here (1.0.3.17; the
// host only, never its path or query, and not personal information in Steve's
// words, so it is as public as the rest of the tab). No user agent, no
// account, no email, no full address, no query string. A row that has no
// field for a thing cannot leak it.

/// <summary>One request as the activity feature sees it. The type has no room for anything a person could be named by.</summary>
public sealed record ActivityHit(DateTimeOffset At, string Visitor, string Network, string Path, string Store, bool Bot, string? Source = null);

/// <summary>One store's requests in one UTC hour, with the paths it served most, for the graph.</summary>
public sealed record ActivityHour(string Store, DateTimeOffset Hour, int Requests, int Bots, IReadOnlyDictionary<string, int> Paths);

/// <summary>One visitor token on one store on one UTC day, for the table nobody anonymous can read.</summary>
public sealed record ActivityVisitor(
    string Store,
    string Day,
    string Visitor,
    string Network,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int Requests,
    int Bots,
    IReadOnlyDictionary<string, int> Paths,
    IReadOnlyDictionary<string, int>? Sources = null);

/// <summary>Whether a store can keep activity right now, and if not, why, in one sentence a page can show.</summary>
public sealed record ActivityAvailability(bool Available, string Reason);

/// <summary>Port: where activity is kept. Writes arrive in batches, never from a request thread.</summary>
public interface IActivityStore
{
    Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation);

    Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation);

    Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation);

    Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation);
}

/// <summary>The port wired to nothing: a store that did not come up keeps no activity and says so.</summary>
public sealed class NullActivityStore(string reason) : IActivityStore
{
    public static readonly NullActivityStore Instance = new("this store keeps no activity");

    public Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new ActivityAvailability(false, reason));

    public Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation) => Task.CompletedTask;

    public Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<ActivityHour>>([]);

    public Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<ActivityVisitor>>([]);
}
// #endregion activity-port

// #region activity-folding
/// <summary>
/// A batch of hits folded into the deltas a store applies: one per store and
/// hour, one per store, day and visitor. Pure, so it is tested without a store,
/// and shared, so the two adapters cannot fold the same batch two ways.
/// </summary>
public static class ActivityFolding
{
    /// <summary>
    /// How many paths a delta carries, by count. A hit-and-run scanner touches
    /// hundreds of paths in a minute, and a row that kept all of them would be a
    /// row that grows without bound; the top few say what the traffic was.
    /// </summary>
    public const int PathsKept = 8;

    /// <summary>How many paths a stored row keeps after the merge, for the same reason.</summary>
    public const int PathsStored = 20;

    public static IReadOnlyList<ActivityHourDelta> Hours(IEnumerable<ActivityHit> hits) =>
        hits.GroupBy(hit => (hit.Store, Hour: HourOf(hit.At)))
            .Select(group => new ActivityHourDelta(
                group.Key.Store,
                group.Key.Hour,
                group.Count(),
                group.Count(hit => hit.Bot),
                TopPaths(group)))
            .OrderBy(delta => delta.Store, StringComparer.Ordinal)
            .ThenBy(delta => delta.Hour)
            .ToList();

    public static IReadOnlyList<ActivityVisitorDelta> Visitors(IEnumerable<ActivityHit> hits) =>
        hits.GroupBy(hit => (hit.Store, Day: DayOf(hit.At), hit.Visitor))
            .Select(group => new ActivityVisitorDelta(
                group.Key.Store,
                group.Key.Day,
                group.Key.Visitor,
                group.First().Network,
                group.Min(hit => hit.At),
                group.Max(hit => hit.At),
                group.Count(),
                group.Count(hit => hit.Bot),
                TopPaths(group),
                TopSources(group)))
            .OrderBy(delta => delta.Store, StringComparer.Ordinal)
            .ThenBy(delta => delta.Day, StringComparer.Ordinal)
            .ThenBy(delta => delta.Visitor, StringComparer.Ordinal)
            .ToList();

    /// <summary>Stored paths plus a delta's paths, kept to the top few by count.</summary>
    public static IReadOnlyDictionary<string, int> Merge(IReadOnlyDictionary<string, int> stored, IReadOnlyList<KeyValuePair<string, int>> added)
    {
        var merged = stored.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var (path, count) in added)
        {
            merged[path] = merged.GetValueOrDefault(path) + count;
        }

        return merged
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(PathsStored)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    public static DateTimeOffset HourOf(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The UTC day as text, which is what both stores key and partition a visitor on.</summary>
    public static string DayOf(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The hosts that linked here, by page loads, kept to the top few like the paths.</summary>
    private static IReadOnlyList<KeyValuePair<string, int>> TopSources(IEnumerable<ActivityHit> hits) =>
        hits.Where(hit => hit.Source is not null)
            .GroupBy(hit => hit.Source!, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(PathsKept)
            .ToList();

    private static IReadOnlyList<KeyValuePair<string, int>> TopPaths(IEnumerable<ActivityHit> hits) =>
        hits.GroupBy(hit => hit.Path, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(PathsKept)
            .ToList();
}

/// <summary>What one batch adds to one store's hour.</summary>
public sealed record ActivityHourDelta(string Store, DateTimeOffset Hour, int Requests, int Bots, IReadOnlyList<KeyValuePair<string, int>> Paths);

/// <summary>What one batch adds to one visitor's day on one store.</summary>
public sealed record ActivityVisitorDelta(
    string Store,
    string Day,
    string Visitor,
    string Network,
    DateTimeOffset First,
    DateTimeOffset Last,
    int Requests,
    int Bots,
    IReadOnlyList<KeyValuePair<string, int>> Paths,
    IReadOnlyList<KeyValuePair<string, int>> Sources);
// #endregion activity-folding
