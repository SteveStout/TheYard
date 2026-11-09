// A card's live source and the lease that runs it (ADR: Live dials where the source allows). A source reads one
// sample at its own grain; the lease says whether anybody is looking, so a source runs only while a /live read for
// its card arrived in the last minute, and never otherwise.
namespace TheYard.Application.Shared.Live;

// #region live-sources
/// <summary>One live sample: when, and each reading by name.</summary>
/// <param name="At">When it was read.</param>
/// <param name="Values">Each reading by name, null when it could not be read.</param>
public sealed record LiveSample(DateTimeOffset At, IReadOnlyDictionary<string, double?> Values);

/// <summary>A card's live source: its key, its grain, where it reads, and one read.</summary>
public interface ILiveSource
{
    /// <summary>The card's key.</summary>
    string Key { get; }

    /// <summary>How often it reads, in seconds: the source's own grain.</summary>
    int GrainSeconds { get; }

    /// <summary>Where it reads, as the card's source line says it.</summary>
    string Source { get; }

    /// <summary>Why it cannot read in this process, or null when it can.</summary>
    string? Absent { get; }

    /// <summary>One sample, or null when this read found nothing.</summary>
    Task<LiveSample?> SampleAsync(DateTimeOffset now, CancellationToken cancellation);
}

/// <summary>Who is looking: a card is leased for a minute after each read of its live route.</summary>
public sealed class LiveLease
{
    /// <summary>How long a read keeps a card's source running.</summary>
    public static readonly TimeSpan Span = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _touched = new(StringComparer.Ordinal);

    /// <summary>Marks a card read now.</summary>
    public void Touch(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            _touched[key] = now;
        }
    }

    /// <summary>True when the card was read inside the last minute.</summary>
    public bool Leased(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _touched.TryGetValue(key, out var at) && now - at <= Span;
        }
    }
}

/// <summary>Every live source's ring, by card key; a source with no ring yet has never been leased.</summary>
public sealed class LiveRings
{
    /// <summary>How many samples a source's ring holds.</summary>
    public const int Size = 240;

    private readonly object _gate = new();
    private readonly Dictionary<string, LiveRing<LiveSample>> _rings = new(StringComparer.Ordinal);

    /// <summary>The ring for a card, made the first time it is asked for.</summary>
    public LiveRing<LiveSample> For(string key)
    {
        lock (_gate)
        {
            if (!_rings.TryGetValue(key, out var ring))
            {
                ring = new LiveRing<LiveSample>(Size, sample => sample.At);
                _rings[key] = ring;
            }
            return ring;
        }
    }
}
// #endregion live-sources
