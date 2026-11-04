// The review route's reach kept in this process's memory (ADR: Ten minutes, seven stops, the addendum on
// the reach rows): what a host with no document store keeps, a developer's machine and the tests on the
// relational store, gone with the process, the way MemoryLinkOpens keeps the tagged opens.
namespace TheYard.Application.Features.Review;

/// <summary>Rows a visitor a day in this process's memory, folded by the one rule.</summary>
public sealed class MemoryReviewStore : IReviewStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Day, string Hash), ReviewRow> _rows = new();

    /// <summary>Where the reach is kept: here, until the process restarts.</summary>
    public string Source => "kept in this process's memory until it restarts";

    /// <summary>Always: memory is always there.</summary>
    /// <param name="cancellation">Unused.</param>
    public Task<bool> AvailableAsync(CancellationToken cancellation) => Task.FromResult(true);

    /// <summary>Folds each row into the one kept for its visitor and day.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="cancellation">Unused.</param>
    public Task KeepAsync(IReadOnlyList<ReviewRow> rows, CancellationToken cancellation)
    {
        lock (_gate)
        {
            foreach (var row in rows)
            {
                var key = (row.Day, row.Hash);
                _rows[key] = _rows.TryGetValue(key, out var kept) ? ReviewFolding.Merge(kept, row) : row;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>The rows from a day on, oldest first.</summary>
    /// <param name="day">The first day, yyyy-MM-dd.</param>
    /// <param name="cancellation">Unused.</param>
    public Task<IReadOnlyList<ReviewRow>> RowsSinceAsync(string day, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ReviewRow>>([.. _rows.Values.Where(row => string.CompareOrdinal(row.Day, day) >= 0).OrderBy(row => row.Day, StringComparer.Ordinal)]);
        }
    }

    /// <summary>The earliest day held, or null.</summary>
    /// <param name="cancellation">Unused.</param>
    public Task<string?> FirstDayAsync(CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.Count == 0 ? null : _rows.Keys.Min(key => key.Day));
        }
    }
}
