namespace TheYard.Tests;

/// <summary>
/// The month of five-minute buckets (ADR-104, the addendum on the month that was read whole): read whole once, then only
/// from the newest bucket held, trimmed at thirty days, and one read shared by every caller that arrives during it. On
/// 19 November the whole thirty-day read, once a window every ten minutes, drew the memory card's 545 to 1,039 MB sawtooth.
/// </summary>
public class MonthKeepTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_read_takes_the_month_and_a_later_one_only_the_buckets_from_the_newest_held()
    {
        var store = new Store(Nine);
        var keep = new MonthKeep<DateTimeOffset>(bucket => bucket);

        var first = await keep.ReadAsync(Nine, store.SinceAsync, CancellationToken.None);
        Assert.Equal(Nine - MonthKeep<DateTimeOffset>.Month, store.Asked.Single());
        Assert.Equal(30 * 288 + 1, first.Count);

        store.Now = Nine.AddMinutes(12);
        var second = await keep.ReadAsync(Nine.AddMinutes(12), store.SinceAsync, CancellationToken.None);

        Assert.Equal(first[^1], store.Asked[1]);
        Assert.Equal(3, store.Returned[1]);
        Assert.Equal(second.Distinct().Count(), second.Count);
        Assert.Equal(Nine.AddMinutes(10), second[^1]);
        Assert.True(second[0] >= Nine.AddMinutes(12) - MonthKeep<DateTimeOffset>.Month);
        Assert.Equal(second.Order(), second);
    }

    [Fact]
    public async Task A_read_under_ten_minutes_old_asks_the_store_nothing()
    {
        var store = new Store(Nine);
        var keep = new MonthKeep<DateTimeOffset>(bucket => bucket);

        await keep.ReadAsync(Nine, store.SinceAsync, CancellationToken.None);
        await keep.ReadAsync(Nine.AddMinutes(9), store.SinceAsync, CancellationToken.None);

        Assert.Single(store.Asked);
    }

    [Fact]
    public async Task Callers_that_arrive_during_a_read_wait_on_that_one_read()
    {
        var store = new Store(Nine) { Gate = new TaskCompletionSource() };
        var keep = new MonthKeep<DateTimeOffset>(bucket => bucket);

        var reads = Enumerable.Range(0, 8).Select(_ => keep.ReadAsync(Nine, store.SinceAsync, CancellationToken.None)).ToList();
        store.Gate.SetResult();
        var months = await Task.WhenAll(reads);

        Assert.Single(store.Asked);
        Assert.All(months, month => Assert.Same(months[0], month));
    }

    [Fact]
    public async Task A_caller_that_gives_up_leaves_the_read_to_the_others_and_a_failed_read_is_asked_again()
    {
        var store = new Store(Nine) { Gate = new TaskCompletionSource() };
        var keep = new MonthKeep<DateTimeOffset>(bucket => bucket);
        using var gone = new CancellationTokenSource();

        var leaving = keep.ReadAsync(Nine, store.SinceAsync, gone.Token);
        var staying = keep.ReadAsync(Nine, store.SinceAsync, CancellationToken.None);
        await gone.CancelAsync();
        store.Gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        Assert.Equal(30 * 288 + 1, (await staying).Count);

        var failing = new MonthKeep<DateTimeOffset>(bucket => bucket);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ReadAsync(Nine, (_, _) => throw new InvalidOperationException("store down"), CancellationToken.None));
        Assert.Equal(30 * 288 + 1, (await failing.ReadAsync(Nine, store.SinceAsync, CancellationToken.None)).Count);
    }

    /// <summary>A store of five-minute buckets up to its clock, counting what it is asked.</summary>
    private sealed class Store(DateTimeOffset now)
    {
        public DateTimeOffset Now { get; set; } = now;

        public TaskCompletionSource? Gate { get; init; }

        public List<DateTimeOffset> Asked { get; } = [];

        public List<int> Returned { get; } = [];

        public async Task<IReadOnlyList<DateTimeOffset>> SinceAsync(DateTimeOffset since, CancellationToken cancellation)
        {
            Asked.Add(since);
            if (Gate is not null)
            {
                await Gate.Task;
            }
            var start = new DateTimeOffset(since.Ticks - since.Ticks % TimeSpan.FromMinutes(5).Ticks, TimeSpan.Zero);
            var buckets = new List<DateTimeOffset>();
            for (var at = start < since ? start.AddMinutes(5) : start; at <= Now; at = at.AddMinutes(5))
            {
                buckets.Add(at);
            }
            Returned.Add(buckets.Count);
            return buckets;
        }
    }
}
