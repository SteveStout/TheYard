using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// A store that refused at startup is asked again and attached when it
/// answers (ADR: The relational store, the addendum on the second chance).
/// The loop is run here with a wait that returns at once, so a test that
/// covers an hour of asking takes milliseconds.
/// </summary>
public class StoreSecondChanceTests
{
    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static StoreAttachment Attached(Backend backend, DatabaseState state) => new(
        state,
        null,
        null,
        backend.Inventory,
        backend.Bids,
        backend.Activity,
        () => Task.FromResult(true),
        _ => null);

    [Fact]
    public async Task A_store_that_answers_on_the_third_ask_is_attached_once_and_the_asking_stops()
    {
        var backend = FakeBackend.Named("sql", "Azure SQL Database", ready: false);
        int asked = 0;
        int attached = 0;
        var plan = new StoreSecondChance.Plan(
            backend,
            () => Task.FromResult(++asked < 3
                ? new DatabaseState(false, "Azure SQL Database: SqlException")
                : new DatabaseState(true, "Azure SQL Database, found the published schema")),
            state =>
            {
                attached++;
                backend.Attach(Attached(backend, state));
                return Task.CompletedTask;
            });

        int attempts = await StoreSecondChance.TryAsync(plan, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), NoWait, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal(1, attached);
        Assert.True(backend.Ready);
        Assert.Equal("Azure SQL Database, found the published schema", backend.Database.Note);
        Assert.True(await backend.Probe());
    }

    [Fact]
    public async Task A_store_that_is_ready_from_the_start_is_never_asked()
    {
        var backend = FakeBackend.Named("sql", "SQLite", ready: true);
        int asked = 0;
        var plan = new StoreSecondChance.Plan(
            backend,
            () => { asked++; return Task.FromResult(new DatabaseState(true, "SQLite")); },
            _ => Task.CompletedTask);

        int attempts = await StoreSecondChance.TryAsync(plan, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), NoWait, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(0, attempts);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_store_that_never_answers_is_given_up_on_when_the_window_closes_and_the_files_stay()
    {
        var backend = FakeBackend.Named("sql", "Azure SQL Database", ready: false);
        var plan = new StoreSecondChance.Plan(
            backend,
            () => Task.FromResult(new DatabaseState(false, "Azure SQL Database: SqlException")),
            _ => throw new InvalidOperationException("nothing to attach"));

        // A window of zero closes after the first ask; the first ask still happens.
        int attempts = await StoreSecondChance.TryAsync(plan, TimeSpan.FromSeconds(30), TimeSpan.Zero, NoWait, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(1, attempts);
        Assert.False(backend.Ready);
    }

    [Fact]
    public async Task An_attach_that_throws_is_asked_again_rather_than_ending_the_chance()
    {
        var backend = FakeBackend.Named("sql", "Azure SQL Database", ready: false);
        int attachCalls = 0;
        var plan = new StoreSecondChance.Plan(
            backend,
            () => Task.FromResult(new DatabaseState(true, "Azure SQL Database, found the published schema")),
            state =>
            {
                if (++attachCalls == 1)
                {
                    throw new InvalidOperationException("the catalogue read timed out while warming");
                }
                backend.Attach(Attached(backend, state));
                return Task.CompletedTask;
            });

        int attempts = await StoreSecondChance.TryAsync(plan, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), NoWait, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, attachCalls);
        Assert.True(backend.Ready);
    }

    [Fact]
    public async Task Cancellation_ends_the_asking_with_the_files_in_place()
    {
        var backend = FakeBackend.Named("sql", "Azure SQL Database", ready: false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var plan = new StoreSecondChance.Plan(backend, () => throw new InvalidOperationException("never asked"), _ => Task.CompletedTask);

        int attempts = await StoreSecondChance.TryAsync(plan, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), NoWait, NullLogger.Instance, cancelled.Token);

        Assert.Equal(0, attempts);
        Assert.False(backend.Ready);
    }

    // #region five-tries
    [Fact]
    public async Task A_store_is_asked_five_times_at_startup_and_the_fourth_refusal_is_not_the_end()
    {
        int asked = 0;
        var waits = new List<TimeSpan>();
        var state = await StorePrepare.WithTriesAsync(
            () => Task.FromResult(++asked < 5
                ? new DatabaseState(false, "Azure SQL Database: SqlException")
                : new DatabaseState(true, "Azure SQL Database, found the published schema") { SchemaMs = 12 }),
            wait => { waits.Add(wait); return Task.CompletedTask; });

        Assert.Equal(5, asked);
        Assert.True(state.Ready);
        Assert.Equal("Azure SQL Database, found the published schema, on ask 5 of 5", state.Note);
        Assert.Equal(12, state.SchemaMs);
        // The waits double from two seconds: about thirty seconds in all, inside the deploy's five minutes.
        Assert.Equal(new[] { 2, 4, 8, 16 }, waits.Select(wait => (int)wait.TotalSeconds).ToArray());
    }

    [Fact]
    public async Task A_store_that_refuses_five_times_is_written_off_with_the_count_and_the_last_reason()
    {
        int asked = 0;
        var state = await StorePrepare.WithTriesAsync(
            () => { asked++; return Task.FromResult(new DatabaseState(false, "Azure SQL Database: SqlException", new InvalidOperationException("login"))); },
            _ => Task.CompletedTask);

        Assert.Equal(5, asked);
        Assert.False(state.Ready);
        Assert.Equal("Azure SQL Database: SqlException, refused 5 times", state.Note);
        Assert.NotNull(state.Failure);
    }

    [Fact]
    public async Task A_store_that_answers_first_time_is_asked_once_and_its_note_is_untouched()
    {
        int asked = 0;
        var state = await StorePrepare.WithTriesAsync(
            () => { asked++; return Task.FromResult(new DatabaseState(true, "SQLite, migrated")); },
            _ => throw new InvalidOperationException("no wait"));

        Assert.Equal(1, asked);
        Assert.Equal("SQLite, migrated", state.Note);
        Assert.Equal(5, StorePrepare.Tries);
    }
    // #endregion five-tries

    [Fact]
    public void The_second_chance_asks_every_half_minute_for_an_hour()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), StoreSecondChance.Every);
        Assert.Equal(TimeSpan.FromHours(1), StoreSecondChance.Window);
    }
}
