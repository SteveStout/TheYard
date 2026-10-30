using Microsoft.EntityFrameworkCore;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The relational activity adapter against a real engine. SQLite is the engine for the reason it is
/// the engine in <c>RelationalConstraintTests</c>: it is the one this suite can make and throw away
/// in a millisecond on a machine with no cloud credential, and the folding this adapter exists for is
/// provider-independent.
///
/// What is worth holding here is the fold, not the round trip. The whole point of the adapter is that
/// however many requests arrive, one hour is one row and one visitor-day is one row, so the Admin
/// tab answers from counters rather than from a row per request; a second batch for the same hour
/// must move that row by its delta rather than add another. A reader who changed the adapter to
/// insert per hit would see every assertion below fail on the counts, which is what this file is for.
/// </summary>
public sealed class EfActivityStoreTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), $"theyard-activity-{Guid.NewGuid():N}.db");

    /// <summary>Pooling off, so the file can be deleted without a process-wide ClearAllPools.</summary>
    private YardConnection Connection => new(YardProvider.Sqlite, $"Data Source={_file};Pooling=False");

    private EfActivityStore Store => new(new PlainFactory(Connection));

    public EfActivityStoreTests()
    {
        using var db = new YardDbContext(Connection.Options());
        db.Database.Migrate();
    }

    private static ActivityHit Hit(DateTimeOffset at, string visitor, string path, bool bot = false, string? source = null) =>
        new(at, visitor, "203.0.113.x", path, "sql", bot, source);

    // #region availability
    /// <summary>
    /// The migration made the tables, so the store says it can keep activity and names where. The
    /// retention line is part of the answer because the tab shows it beside the counts: a relational
    /// store keeps them until somebody deletes them, which is a different promise from the document
    /// store's expiry and has to be readable as one.
    /// </summary>
    [Fact]
    public async Task A_migrated_database_can_keep_activity_and_says_where_and_for_how_long()
    {
        var said = await Store.AvailabilityAsync(CancellationToken.None);

        Assert.Equal((true, "kept in SQLite", "kept in SQLite with no expiry"), (said.Available, said.Reason, said.Retention));
    }

    /// <summary>
    /// A database that is not there refuses the store once, by the exception's type and never its
    /// message, for the reason the health check gives: a driver's message names the server.
    /// </summary>
    [Fact]
    public async Task A_database_that_cannot_be_asked_is_refused_by_the_exceptions_type_and_not_its_message()
    {
        var store = new EfActivityStore(new PlainFactory(new YardConnection(YardProvider.Sqlite, "Data Source=/this/path/does/not/exist/x.db;Pooling=False")));

        var said = await store.AvailabilityAsync(CancellationToken.None);

        Assert.False(said.Available);
        Assert.StartsWith("the store could not be asked: ", said.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("/this/path/does/not/exist", said.Reason, StringComparison.Ordinal);
    }
    // #endregion availability

    // #region folding
    /// <summary>
    /// Two batches of hits in the same hour by the same visitor leave one hour row and one visitor
    /// row, moved by their deltas rather than added to. The bot count and the paths ride the same
    /// fold, and the paths come back with a count each so the graph can show what was served most.
    /// </summary>
    [Fact]
    public async Task Hits_in_one_hour_fold_into_one_row_that_a_second_batch_moves_rather_than_doubles()
    {
        var store = Store;
        var hour = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero);
        string visitor = "0123456789abcdef0123456789abcdef";

        await store.RecordAsync(
            [Hit(hour.AddMinutes(1), visitor, "/"), Hit(hour.AddMinutes(2), visitor, "/"), Hit(hour.AddMinutes(3), visitor, "/api/vehicles", bot: true)],
            CancellationToken.None);
        await store.RecordAsync([Hit(hour.AddMinutes(40), visitor, "/api/vehicles")], CancellationToken.None);

        var hours = await store.HoursAsync(hour.AddHours(-1), CancellationToken.None);
        var kept = Assert.Single(hours, row => row.Hour == hour);
        Assert.Equal((4, 1), (kept.Requests, kept.Bots));
        Assert.Equal(2, kept.Paths["/"]);
        Assert.Equal(2, kept.Paths["/api/vehicles"]);

        var visitors = await store.VisitorsAsync(hour.AddHours(-1), CancellationToken.None);
        var person = Assert.Single(visitors, row => row.Visitor == visitor);
        Assert.Equal((4, 1, "2026-10-30", "203.0.113.x"), (person.Requests, person.Bots, person.Day, person.Network));
        // First and last seen span both batches, so the row is the day and not the batch.
        Assert.Equal(hour.AddMinutes(1), person.FirstSeen);
        Assert.Equal(hour.AddMinutes(40), person.LastSeen);
    }

    /// <summary>
    /// Two hours are two rows and two days are two visitor rows, and a window that begins after a hit
    /// does not return it: the window is the question the tab asks, and an adapter that ignored it
    /// would show a graph that never ends.
    /// </summary>
    [Fact]
    public async Task Hours_and_days_are_rows_of_their_own_and_the_window_is_obeyed()
    {
        var store = Store;
        var first = new DateTimeOffset(2026, 10, 29, 23, 30, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 10, 30, 0, 30, 0, TimeSpan.Zero);

        await store.RecordAsync([Hit(first, "aaaa", "/"), Hit(second, "aaaa", "/")], CancellationToken.None);

        Assert.Equal(2, (await store.HoursAsync(first.AddHours(-1), CancellationToken.None)).Count);
        var days = await store.VisitorsAsync(first.AddHours(-1), CancellationToken.None);
        Assert.Equal(["2026-10-29", "2026-10-30"], days.Where(row => row.Visitor == "aaaa").Select(row => row.Day).Order());

        // A window that starts after the first hit leaves it behind.
        Assert.Single(await store.HoursAsync(second.AddMinutes(-1), CancellationToken.None));
    }

    /// <summary>An empty batch is not a round trip at all, and a store with nothing kept answers with an empty list rather than null.</summary>
    [Fact]
    public async Task An_empty_batch_keeps_nothing_and_an_empty_store_reads_back_empty()
    {
        var store = Store;

        await store.RecordAsync([], CancellationToken.None);

        Assert.Empty(await store.HoursAsync(DateTimeOffset.UnixEpoch, CancellationToken.None));
        Assert.Empty(await store.VisitorsAsync(DateTimeOffset.UnixEpoch, CancellationToken.None));
    }

    /// <summary>The hosts that linked a visitor here are folded into the visitor's row, so a page load with a referer adds one and a hit with none adds nothing.</summary>
    [Fact]
    public async Task The_hosts_that_linked_a_visitor_here_are_folded_into_the_visitors_row()
    {
        var store = Store;
        var at = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero);

        await store.RecordAsync(
            [Hit(at, "bbbb", "/", source: "news.ycombinator.com"), Hit(at.AddMinutes(1), "bbbb", "/", source: "news.ycombinator.com"), Hit(at.AddMinutes(2), "bbbb", "/")],
            CancellationToken.None);

        var person = Assert.Single(await store.VisitorsAsync(at.AddHours(-1), CancellationToken.None), row => row.Visitor == "bbbb");
        Assert.NotNull(person.Sources);
        Assert.Equal(2, person.Sources["news.ycombinator.com"]);
    }
    // #endregion folding

    /// <summary>The smallest thing that satisfies IDbContextFactory, so the store can be built without a service provider.</summary>
    private sealed class PlainFactory(YardConnection connection) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => new(connection.Options());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (IOException)
            {
                // A handle the engine has not let go of yet; the temp folder keeps it.
            }
        }
    }
}
