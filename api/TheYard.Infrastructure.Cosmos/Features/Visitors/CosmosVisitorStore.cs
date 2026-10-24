// The people who have visited, kept in Azure Cosmos DB (ADR-116: People who have visited, counted once). One
// document a visitor, in the activity container's own partition named "visitors", under a kind of its own:
// the shared database holds twenty-five containers and refuses a twenty-sixth, so a new keep shares a
// container under its own kind before it asks for one (CosmosDefinitionTests). The activity queries ask for
// their two kinds by name, so they never read these. The document holds the hash, the first and last day,
// the days seen and who it was; never the address, never the user agent.
using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Features.Visitors;
using TheYard.Domain.Features.Visitors;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Features.Visitors;

// #region cosmos-visitor-store
/// <summary>The visitors' adapter over the activity container: a point read and a write a visitor a day, and a tally read once a minute at most.</summary>
/// <param name="store">The document store.</param>
/// <param name="clock">The clock the tally's age is read on.</param>
public sealed class CosmosVisitorStore(CosmosStore store, TimeProvider clock) : IVisitorStore
{
    /// <summary>The partition every visitor document is kept in: the activity container's key is the day, and this is not one.</summary>
    public const string Partition = "visitors";

    /// <summary>How long a read tally is answered before the store is asked again.</summary>
    public static readonly TimeSpan TallyAge = TimeSpan.FromMinutes(1);

    private readonly Container _container = store.ContainerNamed(Containers.Activity);
    // One reference, swapped whole, so a reader on another thread sees the last read entire or not at all.
    private TallyRead? _read;
    private bool _available;

    /// <summary>One read of the partition and when it was taken.</summary>
    private sealed record TallyRead(DateTimeOffset At, List<VisitorDocument> Visitors);

    /// <inheritdoc/>
    public string Source => "Cosmos DB, the activity container's visitors";

    /// <inheritdoc/>
    public async Task<bool> AvailableAsync(CancellationToken cancellation)
    {
        // Read once: a container that answered is there, and a read that fails after says so by failing.
        if (_available)
        {
            return true;
        }
        try
        {
            await _container.ReadContainerAsync(cancellationToken: cancellation);
            _available = true;
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task KeepAsync(IReadOnlyList<VisitorSighting> sightings, CancellationToken cancellation)
    {
        var key = new PartitionKey(Partition);
        foreach (var seen in sightings)
        {
            var kept = await ReadAsync(seen.Hash, key, cancellation);
            var next = kept is null
                ? (First: seen.Day, Last: seen.Day, Days: 1, Kind: seen.Kind)
                : VisitorRecords.Seen((kept.FirstSeen, kept.LastSeen, kept.DaysSeen, VisitorKinds.FromWord(kept.Who)), seen);
            if (kept is not null && next == (kept.FirstSeen, kept.LastSeen, kept.DaysSeen, VisitorKinds.FromWord(kept.Who)))
            {
                continue;
            }
            await _container.UpsertItemAsync(
                new VisitorDocument { Id = seen.Hash, FirstSeen = next.First, LastSeen = next.Last, DaysSeen = next.Days, Who = VisitorKinds.Word(next.Kind) },
                key,
                cancellationToken: cancellation);
        }
    }

    /// <summary>One kept visitor by its hash, or null when it has not been seen before.</summary>
    private async Task<VisitorDocument?> ReadAsync(string hash, PartitionKey key, CancellationToken cancellation)
    {
        try
        {
            return (await _container.ReadItemAsync<VisitorDocument>(hash, key, cancellationToken: cancellation)).Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<VisitorTally> TallyAsync(string since, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        var read = _read;
        if (read is null || now - read.At > TallyAge)
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.day = @day AND c.kind = @kind")
                .WithParameter("@day", Partition)
                .WithParameter("@kind", VisitorDocument.KindName);
            var visitors = new List<VisitorDocument>();
            using var feed = _container.GetItemQueryIterator<VisitorDocument>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(Partition) });
            while (feed.HasMoreResults)
            {
                visitors.AddRange(await feed.ReadNextAsync(cancellation));
            }
            _read = read = new TallyRead(now, visitors);
        }
        var people = read.Visitors.Where(v => VisitorKinds.FromWord(v.Who) == VisitorKind.People).ToList();
        return new VisitorTally(
            people.Count,
            read.Visitors.Count(v => VisitorKinds.FromWord(v.Who) == VisitorKind.Scanners),
            people.Count == 0 ? null : people.Min(v => v.FirstSeen),
            people.Where(v => string.CompareOrdinal(v.FirstSeen, since) >= 0)
                .GroupBy(v => v.FirstSeen, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }
}

/// <summary>One visitor as it is kept: the hash, the first and last UTC day, the days seen and who it was.</summary>
public sealed class VisitorDocument
{
    /// <summary>The value of <see cref="Kind"/> on every visitor record, which the activity queries never ask for.</summary>
    public const string KindName = "visitor-record";

    /// <summary>The visitor's hash, keyed, of the address and the user agent (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Always <see cref="KindName"/> (<c>kind</c>).</summary>
    public string Kind { get; set; } = KindName;

    /// <summary>The partition, always <see cref="CosmosVisitorStore.Partition"/> (<c>day</c>).</summary>
    public string Day { get; set; } = CosmosVisitorStore.Partition;

    /// <summary>The first UTC day the visitor was seen, yyyy-MM-dd (<c>first_seen</c>).</summary>
    public string FirstSeen { get; set; } = "";

    /// <summary>The latest UTC day the visitor was seen (<c>last_seen</c>).</summary>
    public string LastSeen { get; set; } = "";

    /// <summary>How many UTC days the visitor was seen on (<c>days_seen</c>).</summary>
    public int DaysSeen { get; set; }

    /// <summary>"person", "scanner" or "self" (<c>who</c>).</summary>
    public string Who { get; set; } = "";
}
// #endregion cosmos-visitor-store
