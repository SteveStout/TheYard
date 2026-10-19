// The one cache shape this application uses for anything a request fills: bounded by a count and
// by an estimate of the bytes it holds, every entry expiring, the least recently used going first
// when either bound is reached. A dictionary that only grows is a leak with a delay (ADR: The store
// does the work, rule b), and this is the type the memory test looks for.

namespace TheYard.Application;

/// <summary>
/// A least-recently-used cache with two bounds, a count and a byte estimate, and an expiry on
/// every entry. Thread-safe by one lock: every operation is a dictionary lookup and a linked
/// list move, so the lock is held for less time than a context switch costs.
/// </summary>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TValue">The value.</typeparam>
public sealed class BoundedCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _recency = new();
    private readonly Dictionary<TKey, Task<TValue>> _inflight;
    private readonly Func<TValue, long> _bytesOf;
    private readonly TimeProvider _clock;
    private long _bytes;

    /// <summary>A cache that holds at most <paramref name="maxCount"/> entries and about <paramref name="maxBytes"/> bytes, each for <paramref name="expiry"/>.</summary>
    /// <param name="maxCount">The most entries kept.</param>
    /// <param name="maxBytes">The most bytes kept, by <paramref name="bytesOf"/>'s estimate.</param>
    /// <param name="expiry">How long an entry is answered after it was put.</param>
    /// <param name="bytesOf">An estimate of one value's size.</param>
    /// <param name="clock">The clock expiry is measured on; the system's unless a test hands in its own.</param>
    /// <param name="comparer">The key comparer, or the default.</param>
    public BoundedCache(int maxCount, long maxBytes, TimeSpan expiry, Func<TValue, long> bytesOf, TimeProvider? clock = null, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        MaxCount = maxCount;
        MaxBytes = maxBytes;
        Expiry = expiry;
        _bytesOf = bytesOf;
        _clock = clock ?? TimeProvider.System;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
        _inflight = new Dictionary<TKey, Task<TValue>>(comparer);
    }

    /// <summary>The most entries this cache keeps.</summary>
    public int MaxCount { get; }

    /// <summary>The most bytes this cache keeps, by its estimate.</summary>
    public long MaxBytes { get; }

    /// <summary>How long an entry is answered.</summary>
    public TimeSpan Expiry { get; }

    /// <summary>How many entries it holds now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>The estimate of the bytes it holds now.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>The value under a key when it is there and not expired, which also makes it the most recently used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (_clock.GetUtcNow() < node.Value.ExpiresAt)
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
                Drop(node);
            }
        }
        value = default!;
        return false;
    }

    /// <summary>Puts a value under a key, then evicts the least recently used until both bounds hold. A value larger than the whole byte bound is not kept.</summary>
    public void Set(TKey key, TValue value)
    {
        long bytes = Math.Max(1, _bytesOf(value));
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                Drop(existing);
            }
            if (bytes > MaxBytes)
            {
                return;
            }
            var node = _recency.AddFirst(new Entry(key, value, bytes, _clock.GetUtcNow() + Expiry));
            _map[key] = node;
            _bytes += bytes;
            while (_map.Count > MaxCount || _bytes > MaxBytes)
            {
                Drop(_recency.Last!);
            }
        }
    }

    /// <summary>
    /// The value under a key, or the factory's answer put there first. One load a key: callers that
    /// miss while a load for their key is under way wait for that load rather than starting their
    /// own, so a listing whose five-second bucket has just turned costs the store one count, not
    /// one for every visitor who asked in the same moment.
    /// </summary>
    public async Task<TValue> GetOrAddAsync(TKey key, Func<Task<TValue>> factory)
    {
        if (TryGet(key, out var cached))
        {
            return cached;
        }
        TaskCompletionSource<TValue>? mine = null;
        Task<TValue> load;
        lock (_gate)
        {
            if (!_inflight.TryGetValue(key, out load!))
            {
                mine = new TaskCompletionSource<TValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                load = mine.Task;
                _inflight[key] = load;
            }
        }
        if (mine is null)
        {
            return await load;
        }
        try
        {
            var made = await factory();
            Set(key, made);
            mine.SetResult(made);
            return made;
        }
        catch (Exception ex)
        {
            mine.SetException(ex);
            // Observed here, so a failed load nobody else waited on is not reported again later.
            _ = mine.Task.Exception;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _inflight.Remove(key);
            }
        }
    }

    /// <summary>Empties the cache.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _recency.Clear();
            _bytes = 0;
        }
    }

    private void Drop(LinkedListNode<Entry> node)
    {
        _recency.Remove(node);
        _map.Remove(node.Value.Key);
        _bytes -= node.Value.Bytes;
    }

    private sealed record Entry(TKey Key, TValue Value, long Bytes, DateTimeOffset ExpiresAt);
}
