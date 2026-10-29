// The visits by people the site has kept since it began keeping any, for the first screen's count and the
// Activity card (ADR-116, its addendum on the kept visits). The exact count of people began later, and the
// activity kept before it holds a token that rotates daily, so it cannot say how many different people came;
// what it can say exactly is how many visits people made, one per person per day. Its own file because it is
// one read of the whole kept history, an hour old at most, taken off the request path so no visitor waits on it.
using TheYard.Application.Features.Visitors;

namespace TheYard.Api.Shared.Activity;

// #region visit-history
/// <summary>
/// Counts the visits by people across every kept day of activity, at most once an hour, in the background, and
/// today's again every minute between, so today's visits never run behind today's people (one partition read).
/// A reader is handed the last count at once, or null before the first one finishes; the first read starts it.
/// The first count reads every kept day; each count after it reads only from the day before the newest day
/// it holds, because a day's rows can still be written at its boundary and today's are being written, and
/// every earlier day is as it was. So the hourly read stays two days of rows however long the site keeps them.
/// </summary>
/// <param name="store">The activity store that keeps the visitor rows with no expiry.</param>
/// <param name="clock">The server's clock.</param>
public sealed class VisitHistory(IActivityStore store, TimeProvider clock) : IVisitHistory
{
    /// <summary>How long a count is handed out before it is taken again.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>How long today's visits are handed out before today alone is counted again.</summary>
    public static readonly TimeSpan TodayEvery = TimeSpan.FromMinutes(1);

    /// <summary>How long after a count that failed the next is tried: sooner than an hour, never at once.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>Before the site kept any activity, so a read from here is the whole history.</summary>
    public static readonly DateTimeOffset Beginning = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private KeptVisits? _last;
    private DateTimeOffset _takenAt = DateTimeOffset.MinValue;
    private DateTimeOffset _todayAt = DateTimeOffset.MinValue;
    private Task? _taking;

    /// <summary>The count as last taken, starting a new one when it is older than <see cref="Every"/>.</summary>
    public Task<KeptVisits?> ReadAsync(CancellationToken cancellation)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (_taking is not { IsCompleted: false })
            {
                if (now - _takenAt >= Every)
                {
                    _taking = Task.Run(() => TakeAsync(todayOnly: false, cancellation), CancellationToken.None);
                }
                else if (_last is not null && now - _todayAt >= TodayEvery)
                {
                    _taking = Task.Run(() => TakeAsync(todayOnly: true, cancellation), CancellationToken.None);
                }
            }
            return Task.FromResult(_last);
        }
    }

    /// <summary>The count being taken, while one is; a test awaits it.</summary>
    public Task Taking
    {
        get
        {
            lock (_gate)
            {
                return _taking ?? Task.CompletedTask;
            }
        }
    }

    /// <param name="todayOnly">Whether to ask the store for today alone or for every day since the last answer.</param>
    /// <param name="cancellation">The token of the request that STARTED this take, lent to it. A
    /// later reader waiting on the same take is not the one who began it and does not stop it, which
    /// is the rule shared once-per-process work follows here (ADR: The ports learn to wait).</param>
    private async Task TakeAsync(bool todayOnly, CancellationToken cancellation)
    {
        KeptVisits? held;
        lock (_gate)
        {
            held = _last;
        }
        try
        {
            var since = held is null ? Beginning
                : todayOnly ? new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero)
                : DayBefore(held.ByDay.Keys.Max(StringComparer.Ordinal)!);
            var rows = await store.VisitorsAsync(since, cancellation);
            if (todayOnly && held is not null)
            {
                // Today alone: an empty answer is a quiet start of the day or a store that is not there, and either
                // way the held count stands; a person's row today replaces today's figure.
                var today = rows.Count == 0 ? held : Merge(held, Count(rows), since);
                lock (_gate)
                {
                    _last = today;
                    _todayAt = clock.GetUtcNow();
                }
                return;
            }
            if (rows.Count == 0 && held is not null)
            {
                // A store that answers nothing for two days it answered before is a store that is not there
                // (the activity port answers an empty list when it is unavailable): the held count stands,
                // and the next count waits RetryAfter rather than reading the held days as empty.
                lock (_gate)
                {
                    _takenAt = clock.GetUtcNow() - Every + RetryAfter;
                }
                return;
            }
            var fresh = Count(rows);
            var total = held is null ? fresh : Merge(held, fresh, since);
            lock (_gate)
            {
                _last = total ?? _last;
                _takenAt = clock.GetUtcNow();
                _todayAt = _takenAt;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The count is a figure on a page, never the page: the last one stands, and the next count waits
            // RetryAfter rather than starting at the next read, so a store that is refusing is not asked every
            // fifteen seconds for the whole history.
            lock (_gate)
            {
                if (todayOnly)
                {
                    _todayAt = clock.GetUtcNow() - TodayEvery + RetryAfter;
                }
                else
                {
                    _takenAt = clock.GetUtcNow() - Every + RetryAfter;
                }
            }
        }
    }

    /// <summary>The UTC day before a yyyy-MM-dd day, as the start of that day.</summary>
    private static DateTimeOffset DayBefore(string day) =>
        new(DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(-1), TimeOnly.MinValue, TimeSpan.Zero);

    /// <summary>
    /// The held count with the days read again replaced by the fresh read, from <paramref name="since"/> on: a day
    /// read again with no person in it is a day with no visits, and the days before the read stay as they were.
    /// </summary>
    public static KeptVisits Merge(KeptVisits held, KeptVisits? fresh, DateTimeOffset since)
    {
        string from = since.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var byDay = held.ByDay.Where(day => string.CompareOrdinal(day.Key, from) < 0).ToDictionary(day => day.Key, day => day.Value, StringComparer.Ordinal);
        foreach (var (day, visits) in fresh?.ByDay ?? new Dictionary<string, int>(StringComparer.Ordinal))
        {
            byDay[day] = visits;
        }
        return new KeptVisits(byDay.Values.Sum(), byDay.Keys.Min(StringComparer.Ordinal)!, byDay);
    }

    /// <summary>
    /// Visits by people in these rows: each day's visitors by the rule the Activity card counts with (the site's
    /// own network is self, a visitor whose every request looked like a bot is a scanner, anyone else a person,
    /// every loopback row on a day one machine), the people summed over the days. Null when no person was kept.
    /// </summary>
    public static KeptVisits? Count(IEnumerable<ActivityVisitor> rows)
    {
        var kinds = new Dictionary<string, VisitorKind>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string name = row.Day + "|" + (Hits.IsLoopbackNetwork(row.Network) ? "loopback" : row.Visitor);
            var kind = VisitorKinds.Of(Hits.IsSelfNetwork(row.Network), row.Requests, row.Bots);
            kinds[name] = kinds.TryGetValue(name, out var seen) ? VisitorKinds.Stronger(seen, kind) : kind;
        }
        var byDay = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, kind) in kinds)
        {
            if (kind == VisitorKind.People)
            {
                string day = name[..name.IndexOf('|')];
                byDay[day] = byDay.GetValueOrDefault(day) + 1;
            }
        }
        return byDay.Count == 0 ? null : new KeptVisits(byDay.Values.Sum(), byDay.Keys.Min(StringComparer.Ordinal)!, byDay);
    }
}
// #endregion visit-history
