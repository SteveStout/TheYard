// The one bounded ring every live reading is held in (ADR: Live dials where the source allows): a fixed number of
// samples under one lock, the oldest dropped on every push, read back by age. The box's live samples and every card's
// live source use it, so there is one ring type, not one a card.
namespace TheYard.Application.Shared.Live;

// #region live-ring
/// <summary>A bounded ring of samples, oldest dropped, read back by how old they are.</summary>
/// <typeparam name="T">The sample.</typeparam>
/// <param name="size">How many samples it holds.</param>
/// <param name="at">When a sample was read.</param>
public sealed class LiveRing<T>(int size, Func<T, DateTimeOffset> at)
{
    private readonly object _gate = new();
    private readonly Queue<T> _ring = new();

    /// <summary>How many samples the ring holds at most.</summary>
    public int Size => size;

    /// <summary>When the newest sample was read, or null before the first.</summary>
    public DateTimeOffset? NewestAt
    {
        get
        {
            lock (_gate)
            {
                return _ring.Count == 0 ? null : _ring.Max(at);
            }
        }
    }

    /// <summary>Adds a sample, dropping the oldest past the ring's size.</summary>
    public void Push(T sample)
    {
        lock (_gate)
        {
            _ring.Enqueue(sample);
            while (_ring.Count > size)
            {
                _ring.Dequeue();
            }
        }
    }

    /// <summary>The samples no older than <paramref name="span"/> before the newest, oldest first.</summary>
    public IReadOnlyList<T> Last(TimeSpan span)
    {
        lock (_gate)
        {
            if (_ring.Count == 0)
            {
                return [];
            }
            var newest = _ring.Max(at);
            return [.. _ring.Where(sample => at(sample) >= newest - span).OrderBy(at)];
        }
    }

    /// <summary>Empties the ring.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _ring.Clear();
        }
    }
}
// #endregion live-ring
