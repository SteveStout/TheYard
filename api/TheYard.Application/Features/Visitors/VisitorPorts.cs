// Where the people who have visited are kept (ADR-116: People who have visited, counted once): one record a
// visitor, keyed by a hash that does not rotate, with the day it was first and last seen, how many days it was
// seen and its kind. Never the address, never the user agent: the hash is all a record holds of who it was.
// The reading side and the writing side are two interfaces (ADR-115): the count's query takes the reader.
using TheYard.Domain.Features.Visitors;

namespace TheYard.Application.Features.Visitors;

// #region visitor-ports
/// <summary>One visitor seen on one UTC day, as the collector hands it on: the stable hash, the day and the kind.</summary>
/// <param name="Hash">The visitor's hash: keyed, of the address and the user agent, the same every day.</param>
/// <param name="Day">The UTC day as yyyy-MM-dd.</param>
/// <param name="Kind">What the visitor's requests that day read as.</param>
public sealed record VisitorSighting(string Hash, string Day, VisitorKind Kind);

/// <summary>The kept visitors as the count reads them.</summary>
/// <param name="People">How many visitors are people, all time.</param>
/// <param name="Scanners">How many are scanners or crawlers, left out of the count.</param>
/// <param name="CountedFrom">The first day anything was kept, or null before the first.</param>
/// <param name="FirstSeenByDay">How many people were first seen on each UTC day from the day asked, by yyyy-MM-dd.</param>
public sealed record VisitorTally(int People, int Scanners, string? CountedFrom, IReadOnlyDictionary<string, int> FirstSeenByDay);

/// <summary>The reading side: the tally, and whether the store can keep visitors at all.</summary>
public interface IVisitorReader
{
    /// <summary>Where the visitors are kept, in a phrase a readout can show ("Cosmos DB visitors"), or why nothing is.</summary>
    string Source { get; }

    /// <summary>True when the store can keep and read visitors now.</summary>
    Task<bool> AvailableAsync(CancellationToken cancellation);

    /// <summary>The tally, with the people first seen on each day from <paramref name="since"/> (a UTC day, yyyy-MM-dd).</summary>
    Task<VisitorTally> TallyAsync(string since, CancellationToken cancellation);
}

/// <summary>The writing side: one write a visitor a day at most, off the request path.</summary>
public interface IVisitorStore : IVisitorReader
{
    /// <summary>Keeps these sightings: a new hash is a new visitor; a known one moves its last day and its day count.</summary>
    Task KeepAsync(IReadOnlyList<VisitorSighting> sightings, CancellationToken cancellation);
}

/// <summary>No store that can keep visitors: nothing kept, and every reading says why.</summary>
/// <param name="why">Why nothing is kept, in a phrase a readout can show.</param>
public sealed class NoVisitorStore(string why) : IVisitorStore
{
    /// <inheritdoc/>
    public string Source => why;

    /// <inheritdoc/>
    public Task<bool> AvailableAsync(CancellationToken cancellation) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<VisitorTally> TallyAsync(string since, CancellationToken cancellation) =>
        Task.FromResult(new VisitorTally(0, 0, null, new Dictionary<string, int>(StringComparer.Ordinal)));

    /// <inheritdoc/>
    public Task KeepAsync(IReadOnlyList<VisitorSighting> sightings, CancellationToken cancellation) => Task.CompletedTask;
}

/// <summary>
/// The visitors kept in this process's memory: for the tests, and for a host with no document store, where the
/// count says it lasts only until the process restarts. One record a hash, the same rules as the kept one.
/// </summary>
public sealed class MemoryVisitorStore : IVisitorStore
{
    private readonly Dictionary<string, (string First, string Last, int Days, VisitorKind Kind)> _visitors = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc/>
    public string Source => "this process's memory, until it restarts";

    /// <inheritdoc/>
    public Task<bool> AvailableAsync(CancellationToken cancellation) => Task.FromResult(true);

    /// <inheritdoc/>
    public Task KeepAsync(IReadOnlyList<VisitorSighting> sightings, CancellationToken cancellation)
    {
        lock (_gate)
        {
            foreach (var seen in sightings)
            {
                _visitors[seen.Hash] = _visitors.TryGetValue(seen.Hash, out var kept)
                    ? VisitorRecords.Seen(kept, seen)
                    : (seen.Day, seen.Day, 1, seen.Kind);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<VisitorTally> TallyAsync(string since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            var people = _visitors.Values.Where(v => v.Kind == VisitorKind.People).ToList();
            return Task.FromResult(new VisitorTally(
                people.Count,
                _visitors.Values.Count(v => v.Kind == VisitorKind.Scanners),
                _visitors.Count == 0 ? null : _visitors.Values.Min(v => v.First),
                people.Where(v => string.CompareOrdinal(v.First, since) >= 0).GroupBy(v => v.First, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal)));
        }
    }
}

/// <summary>How a kept visitor moves when it is seen again, the one rule both adapters apply.</summary>
public static class VisitorRecords
{
    /// <summary>
    /// A known visitor seen again: its last day moves forward, a new day adds one to its days, and its kind is the
    /// stronger of the two readings, so a scanner that later reads as a person is counted from then.
    /// </summary>
    public static (string First, string Last, int Days, VisitorKind Kind) Seen((string First, string Last, int Days, VisitorKind Kind) kept, VisitorSighting seen)
    {
        bool later = string.CompareOrdinal(seen.Day, kept.Last) > 0;
        return (kept.First, later ? seen.Day : kept.Last, later ? kept.Days + 1 : kept.Days, VisitorKinds.Stronger(kept.Kind, seen.Kind));
    }
}
// #endregion visitor-ports
