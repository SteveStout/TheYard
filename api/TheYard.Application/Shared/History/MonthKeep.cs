namespace TheYard.Application.Shared.History;

// #region month-keep
/// <summary>
/// A month of five-minute buckets held in memory and brought up to date a few buckets at a time (ADR-104, the addendum
/// on the month that was read whole). Before it every card's month was the store's thirty-day GROUP BY, read whole
/// every ten minutes and once more for each window being kept at that minute, which built and dropped 150 to 450 MB
/// each time and drew the memory card's sawtooth: 545 to 1,039 MB every five minutes at no traffic, the heap at 799 MB
/// of its 986 MB limit. Now the first read takes the month, every later read takes only the buckets from the newest
/// one held (read again whole, since it may have been partial), the month is trimmed at thirty days, and callers that
/// arrive while a read is under way wait on that one read.
/// </summary>
/// <typeparam name="T">A bucket.</typeparam>
/// <param name="at">A bucket's start.</param>
public sealed class MonthKeep<T>(Func<T, DateTimeOffset> at)
{
    /// <summary>How far back the month reaches.</summary>
    public static readonly TimeSpan Month = TimeSpan.FromDays(30);

    /// <summary>How long a month read is served before the newest buckets are read again.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private IReadOnlyList<T> _kept = [];
    private DateTimeOffset _at = DateTimeOffset.MinValue;
    private Task<IReadOnlyList<T>>? _reading;

    /// <summary>The month, read from the store only for what it does not hold yet.</summary>
    /// <param name="now">The reader's clock.</param>
    /// <param name="since">Reads the buckets from a start, oldest first. It runs with no cancellation, so one caller's
    /// cancelled request never fails the read the others are waiting on; the store's own timeouts end it.</param>
    /// <param name="cancellation">Stops this caller's wait, never the read.</param>
    public Task<IReadOnlyList<T>> ReadAsync(DateTimeOffset now, Func<DateTimeOffset, CancellationToken, Task<IReadOnlyList<T>>> since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (now - _at < Fresh)
            {
                return Task.FromResult(_kept);
            }
            if (_reading is not { IsCompleted: false })
            {
                _reading = RefreshAsync(now, since);
            }
            return _reading.WaitAsync(cancellation);
        }
    }

    private async Task<IReadOnlyList<T>> RefreshAsync(DateTimeOffset now, Func<DateTimeOffset, CancellationToken, Task<IReadOnlyList<T>>> since)
    {
        IReadOnlyList<T> kept;
        lock (_gate)
        {
            kept = _kept;
        }
        var from = kept.Count == 0 ? now - Month : at(kept[^1]);
        var fresh = await since(from, CancellationToken.None);
        var cut = now - Month;
        IReadOnlyList<T> month = [.. kept.Where(bucket => at(bucket) >= cut && at(bucket) < from), .. fresh];
        lock (_gate)
        {
            _kept = month;
            _at = now;
        }
        return month;
    }
}
// #endregion month-keep
