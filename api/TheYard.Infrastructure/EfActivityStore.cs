using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;

namespace TheYard.Infrastructure;

/// <summary>
/// Site activity kept in the relational store (ADR: Site activity, and the
/// line an address does not cross). Two counter tables, moved by the deltas
/// a batch folds into.
///
/// <para>The counters move by <c>ExecuteUpdate</c>, so the increment happens
/// in the database and two containers writing the same Azure SQL row add to
/// it rather than each writing the number it read. The paths column is a JSON
/// object of the top twenty paths, read, merged and written back, and that is
/// the one field where a batch can lose to a batch from the other container
/// in the same five seconds; a count of which paths were popular is worth
/// exactly that much and no more, and the record says so.</para>
///
/// <para>The tables are published by a person, like every other table on SQL
/// Server (ADR: Data first, and the database in source control), so this
/// store asks once whether they are there and keeps nothing until they are.
/// On SQLite the migration creates them. A store that cannot keep activity
/// says why on the Admin tab rather than throwing on every batch.</para>
/// </summary>
public sealed class EfActivityStore(IDbContextFactory<YardDbContext> factory) : IActivityStore
{
    private static readonly string[] Required = ["ActivityHours", "ActivityVisitors"];

    private ActivityAvailability? _availability;

    // #region availability
    public async Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }

        try
        {
            using var db = await factory.CreateDbContextAsync(cancellation);
            if (db.Database.IsSqlite())
            {
                // The migration made them; a query proves it without a second list of names.
                await db.ActivityHours.AsNoTracking().Take(1).ToListAsync(cancellation);
                return _availability = new ActivityAvailability(true, "kept in SQLite");
            }

            var present = await db.Database
                .SqlQuery<string>($"SELECT name AS Value FROM sys.tables")
                .ToListAsync(cancellation);
            var missing = Required.Where(table => !present.Contains(table, StringComparer.OrdinalIgnoreCase)).ToList();
            return _availability = missing.Count == 0
                ? new ActivityAvailability(true, "kept in Azure SQL Database")
                : new ActivityAvailability(false, $"the published schema is missing {string.Join(", ", missing)}; publish api/TheYard.Database");
        }
        catch (Exception ex)
        {
            // The type and not the message, for the reason the health check
            // gives: a driver's message names the server.
            return _availability = new ActivityAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }
    // #endregion availability

    // #region record
    public async Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation)
    {
        if (hits.Count == 0 || !(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }

        using var db = await factory.CreateDbContextAsync(cancellation);

        foreach (var delta in ActivityFolding.Hours(hits))
        {
            DateTime hour = delta.Hour.UtcDateTime;
            int moved = await db.ActivityHours
                .Where(row => row.Store == delta.Store && row.Hour == hour)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.Requests, row => row.Requests + delta.Requests)
                        .SetProperty(row => row.Bots, row => row.Bots + delta.Bots),
                    cancellation);
            if (moved == 0)
            {
                await InsertAsync(db, new ActivityHourRow
                {
                    Store = delta.Store,
                    Hour = hour,
                    Requests = delta.Requests,
                    Bots = delta.Bots,
                    Paths = Write(ActivityFolding.Merge(Empty, delta.Paths)),
                }, cancellation);
                continue;
            }

            var stored = await db.ActivityHours.AsNoTracking()
                .Where(row => row.Store == delta.Store && row.Hour == hour)
                .Select(row => row.Paths)
                .FirstOrDefaultAsync(cancellation);
            string merged = Write(ActivityFolding.Merge(Read(stored), delta.Paths));
            await db.ActivityHours
                .Where(row => row.Store == delta.Store && row.Hour == hour)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Paths, merged), cancellation);
        }

        foreach (var delta in ActivityFolding.Visitors(hits))
        {
            DateTime first = delta.First.UtcDateTime;
            DateTime last = delta.Last.UtcDateTime;
            int moved = await db.ActivityVisitors
                .Where(row => row.Store == delta.Store && row.Day == delta.Day && row.Visitor == delta.Visitor)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.Requests, row => row.Requests + delta.Requests)
                        .SetProperty(row => row.Bots, row => row.Bots + delta.Bots)
                        .SetProperty(row => row.LastSeen, row => row.LastSeen > last ? row.LastSeen : last)
                        .SetProperty(row => row.FirstSeen, row => row.FirstSeen < first ? row.FirstSeen : first),
                    cancellation);
            if (moved == 0)
            {
                await InsertAsync(db, new ActivityVisitorRow
                {
                    Store = delta.Store,
                    Day = delta.Day,
                    Visitor = delta.Visitor,
                    Network = delta.Network,
                    FirstSeen = first,
                    LastSeen = last,
                    Requests = delta.Requests,
                    Bots = delta.Bots,
                    Paths = Write(ActivityFolding.Merge(Empty, delta.Paths)),
                }, cancellation);
                continue;
            }

            var stored = await db.ActivityVisitors.AsNoTracking()
                .Where(row => row.Store == delta.Store && row.Day == delta.Day && row.Visitor == delta.Visitor)
                .Select(row => row.Paths)
                .FirstOrDefaultAsync(cancellation);
            string merged = Write(ActivityFolding.Merge(Read(stored), delta.Paths));
            await db.ActivityVisitors
                .Where(row => row.Store == delta.Store && row.Day == delta.Day && row.Visitor == delta.Visitor)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Paths, merged), cancellation);
        }
    }

    /// <summary>
    /// The insert that follows an update that moved nothing. Two containers
    /// can both find the row missing and both insert; the second insert fails
    /// on the key, and the right answer to that is the update the first
    /// container's insert made possible, which the next batch will make. The
    /// loss is one delta on the first hour a row exists, once, and only under
    /// that race.
    /// </summary>
    private static async Task InsertAsync<TRow>(YardDbContext db, TRow row, CancellationToken cancellation) where TRow : class
    {
        try
        {
            db.Add(row);
            await db.SaveChangesAsync(cancellation);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }
    // #endregion record

    // #region read
    public async Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        using var db = await factory.CreateDbContextAsync(cancellation);
        DateTime from = ActivityFolding.HourOf(since).UtcDateTime;
        var rows = await db.ActivityHours.AsNoTracking()
            .Where(row => row.Hour >= from)
            .OrderBy(row => row.Store)
            .ThenBy(row => row.Hour)
            .ToListAsync(cancellation);
        return rows
            .Select(row => new ActivityHour(
                row.Store,
                new DateTimeOffset(DateTime.SpecifyKind(row.Hour, DateTimeKind.Utc)),
                row.Requests,
                row.Bots,
                Read(row.Paths)))
            .ToList();
    }

    public async Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        using var db = await factory.CreateDbContextAsync(cancellation);
        string fromDay = ActivityFolding.DayOf(since);
        var rows = await db.ActivityVisitors.AsNoTracking()
            .Where(row => string.Compare(row.Day, fromDay) >= 0)
            .OrderByDescending(row => row.LastSeen)
            .ToListAsync(cancellation);
        return rows
            .Select(row => new ActivityVisitor(
                row.Store,
                row.Day,
                row.Visitor,
                row.Network,
                new DateTimeOffset(DateTime.SpecifyKind(row.FirstSeen, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(row.LastSeen, DateTimeKind.Utc)),
                row.Requests,
                row.Bots,
                Read(row.Paths)))
            .ToList();
    }
    // #endregion read

    private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, int> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static string Write(IReadOnlyDictionary<string, int> paths) => JsonSerializer.Serialize(paths);
}
