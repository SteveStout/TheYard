// Where the box's readings are held between its posts and the card (ADR: The box reports itself): the
// newest snapshot and the ones the recorder has not yet kept, and a ring of the last two minutes of live
// samples. In this process's memory, like the usage log's: a roll empties them, the agent refills them,
// and the kept minutes in the plan container are the record. One process serves both names, so both read
// the same ring.
using TheYard.Application.Shared.Live;
using TheYard.Data;

namespace TheYard.Application.Features.Admin.Cloud;

// #region box-ports
/// <summary>The box's snapshots: the newest, and the ones accepted since the recorder last took them.</summary>
public interface IBoxSnapshots
{
    /// <summary>Holds a snapshot as the newest, and as one the recorder has not kept.</summary>
    void Accept(BoxSnapshot snapshot);

    /// <summary>The newest snapshot accepted, or null before the first.</summary>
    BoxSnapshot? Newest { get; }

    /// <summary>The snapshots accepted since the last take, oldest first; taking them empties the list.</summary>
    IReadOnlyList<BoxSnapshot> TakeUnrecorded();
}

/// <summary>The box's live samples, a short ring.</summary>
public interface IBoxLive
{
    /// <summary>Adds a sample, dropping the oldest past the ring's size.</summary>
    void Push(BoxLiveSample sample);

    /// <summary>The samples no older than <paramref name="span"/> before the newest, oldest first.</summary>
    IReadOnlyList<BoxLiveSample> Last(TimeSpan span);

    /// <summary>When the newest sample was read, or null before the first.</summary>
    DateTimeOffset? NewestAt { get; }
}

/// <summary>The snapshots in this process's memory; at most an hour of them wait for the recorder.</summary>
public sealed class MemoryBoxSnapshots : IBoxSnapshots
{
    /// <summary>The most snapshots held for the recorder: an hour of one a minute.</summary>
    public const int MostWaiting = 60;

    private readonly object _gate = new();
    private readonly List<BoxSnapshot> _waiting = [];
    private BoxSnapshot? _newest;

    /// <inheritdoc />
    public BoxSnapshot? Newest
    {
        get
        {
            lock (_gate)
            {
                return _newest;
            }
        }
    }

    /// <inheritdoc />
    public void Accept(BoxSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_newest is null || snapshot.At >= _newest.At)
            {
                _newest = snapshot;
            }
            _waiting.Add(snapshot);
            if (_waiting.Count > MostWaiting)
            {
                _waiting.RemoveRange(0, _waiting.Count - MostWaiting);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BoxSnapshot> TakeUnrecorded()
    {
        lock (_gate)
        {
            var taken = _waiting.OrderBy(snapshot => snapshot.At).ToList();
            _waiting.Clear();
            return taken;
        }
    }
}

/// <summary>The live samples in this process's memory, a ring of sixty: two minutes at one every two seconds, held in the one ring type every live reading uses.</summary>
public sealed class MemoryBoxLive : IBoxLive
{
    /// <summary>How many samples the ring holds.</summary>
    public const int Size = 60;

    private readonly LiveRing<BoxLiveSample> _ring = new(Size, sample => sample.At);

    /// <inheritdoc />
    public DateTimeOffset? NewestAt => _ring.NewestAt;

    /// <inheritdoc />
    public void Push(BoxLiveSample sample) => _ring.Push(sample);

    /// <inheritdoc />
    public IReadOnlyList<BoxLiveSample> Last(TimeSpan span) => _ring.Last(span);
}
// #endregion box-ports
