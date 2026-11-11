using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The bill kept across rolls (ADR: What Azure charges, the addendum on keeping the read). Until this version the last
/// read was held in memory only, so every blue-green roll started with no bill: the card read "unread" until the new
/// process's first read landed, and with many rolls on two slots Cost Management answered 429 and the card stayed
/// unread for an hour. These cases build the history the way the site does (<see cref="CostRegistration.KeptHistory"/>)
/// over a real SQLite store, start a "new process" with nothing in its memory, and read what the card would show.
/// </summary>
public sealed class KeptCostsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 19, 22, 0, TimeSpan.Zero);
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-costs-{Guid.NewGuid():N}.db");

    public KeptCostsTests()
    {
        using var db = new YardDbContext(Connection.Options());
        db.Database.Migrate();
    }

    private YardConnection Connection => new(YardProvider.Sqlite, $"Data Source={_file};Pooling=False");

    // #region the-sandbox
    /// <summary>The history a process builds at start, over the relational store this test made.</summary>
    private ICostHistory NewProcess(TimeProvider clock)
    {
        var relational = new RelationalContexts();
        var factory = new PlainFactory(Connection);
        relational.Hold(factory, factory);
        return CostRegistration.KeptHistory(relational, clock);
    }

    private static readonly CostDay[] Days =
    [
        new("2026-11-08", "app-theyard-ss", "microsoft.web/sites", 4.10, "USD"),
        new("2026-11-09", "app-theyard-ss", "microsoft.web/sites", 4.20, "USD"),
        new("2026-11-10", "sql-theyard-ss", "microsoft.sql/servers", 0.50, "USD"),
    ];

    /// <summary>A read kept by an earlier process, <paramref name="ago"/> before now.</summary>
    private async Task KeptBy(TimeSpan ago) =>
        await new EfCostStore(() => new PlainFactory(Connection)).SaveAsync(new CostKept(Days, [], Now - ago), CancellationToken.None);

    private static CostReport Card(CostKept kept, CostStatus status) =>
        CostView.Build("30d", kept, Now, new CostHistoryAvailability(true, "kept"), status.Snapshot());
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>
    /// A roll with a read kept twenty minutes ago: the new process shows the kept bill and does not ask Cost Management,
    /// so a roll spends none of the allowance. The reader here is configured and would go to Azure if asked.
    /// </summary>
    [Fact]
    public async Task A_roll_with_kept_days_shows_the_bill_and_does_not_read_again()
    {
        await KeptBy(TimeSpan.FromMinutes(20));
        var clock = new FixedClock(Now);
        var history = NewProcess(clock);
        var status = new CostStatus();
        var keeping = new CostKeeping(new CostReader("sub", "client", configured: true), history, status, clock, NullLogger<CostKeeping>.Instance);

        var outcome = await keeping.RecordOnceAsync(CancellationToken.None);

        Assert.Equal(CostOutcome.Read, outcome);
        Assert.Equal(TimeSpan.FromMinutes(40), keeping.KeptFor);
        var card = Card(await history.ReadAsync("2026-10-07", CancellationToken.None), status);
        Assert.True(card.Available, card.Note);
        Assert.Equal(8.8, card.MonthToDate, 2);
        Assert.Equal((Now - TimeSpan.FromMinutes(20)).ToUnixTimeMilliseconds(), card.ReadAtMs);
    }

    /// <summary>
    /// A 429 with a bill kept two hours ago: the card shows that bill, with the time it was read and the slow-down note,
    /// never "unread".
    /// </summary>
    [Fact]
    public async Task A_429_with_kept_days_shows_the_kept_bill_as_of_its_read()
    {
        await KeptBy(TimeSpan.FromHours(2));
        var history = NewProcess(new FixedClock(Now));
        var status = new CostStatus();
        status.Set(Now, false, CostReader.NoteFor(HttpStatusCode.TooManyRequests));

        var card = Card(await history.ReadAsync("2026-10-07", CancellationToken.None), status);

        Assert.True(card.Available, card.Note);
        Assert.Equal(8.8, card.MonthToDate, 2);
        Assert.Equal((Now - TimeSpan.FromHours(2)).ToUnixTimeMilliseconds(), card.ReadAtMs);
        Assert.StartsWith("Cost Management asked this site to slow down", card.Note, StringComparison.Ordinal);
    }

    /// <summary>A 429 with nothing ever kept is the one case the card has no bill to show, and it says why.</summary>
    [Fact]
    public async Task A_429_with_nothing_kept_is_unread_and_says_why()
    {
        var history = NewProcess(new FixedClock(Now));
        var status = new CostStatus();
        status.Set(Now, false, CostReader.NoteFor(HttpStatusCode.TooManyRequests));

        var card = Card(await history.ReadAsync("2026-10-07", CancellationToken.None), status);

        Assert.False(card.Available);
        Assert.StartsWith("Cost Management asked this site to slow down", card.Note, StringComparison.Ordinal);
    }

    /// <summary>A read is kept whole and read back whole, and the next read replaces it.</summary>
    [Fact]
    public async Task The_store_keeps_one_read_whole_and_the_next_replaces_it()
    {
        var store = new EfCostStore(() => new PlainFactory(Connection));
        Assert.True((await store.AvailabilityAsync(CancellationToken.None)).Available);
        await store.SaveAsync(new CostKept(Days, [new CostForecastDay("2026-11-30", 3, "USD")], Now), CancellationToken.None);
        await store.SaveAsync(new CostKept([Days[0]], [], Now.AddHours(1)), CancellationToken.None);

        var kept = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(kept);
        Assert.Equal(Days[0], Assert.Single(kept.Days));
        Assert.Empty(kept.Forecast);
        Assert.Equal(Now.AddHours(1), kept.ReadAt);
    }

    /// <summary>Before the relational store is attached the history still holds a read in memory and says so.</summary>
    [Fact]
    public async Task With_no_store_attached_the_read_is_held_in_memory_and_said_so()
    {
        var history = new KeptCosts(() => null, new FixedClock(Now));
        await history.KeepAsync(Days, [], Now, CancellationToken.None);

        var said = await history.AvailabilityAsync(CancellationToken.None);
        var kept = await history.ReadAsync("2026-10-07", CancellationToken.None);

        Assert.True(said.Available);
        Assert.Contains("memory only", said.Reason, StringComparison.Ordinal);
        Assert.Equal(3, kept.Days.Count);
    }
    // #endregion the-cases

    /// <summary>The smallest thing that satisfies IDbContextFactory, so the store can be built without a service provider.</summary>
    private sealed class PlainFactory(YardConnection connection) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => new(connection.Options());
    }

    /// <summary>A clock that stands still.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public void Dispose()
    {
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (IOException)
            {
                // A temporary file left behind is the system's to clear.
            }
        }
    }
}
