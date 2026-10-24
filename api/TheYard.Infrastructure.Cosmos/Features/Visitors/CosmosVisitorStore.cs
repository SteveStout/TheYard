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
    // One reference, swapped whole, so a reader on another thread sees the last tally entire or not at all.
    private TallyHeld? _tally;
    private bool _available;

    /// <summary>One tally, when it was read and the day it was asked from.</summary>
    private sealed record TallyHeld(DateTimeOffset At, string Since, VisitorTally Tally);

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
                new VisitorDocument { Id = seen.Hash, FirstSeen = next.First, LastSeen = next.Last, DaysSeen = next.Days, Who = VisitorKinds.Word(next.Kind), Ttl = VisitorDocument.TtlFor(next.Kind) },
                key,
                cancellationToken: cancellation);
            if (kept is null && next.Kind == VisitorKind.Scanners)
            {
                await CountScannerAsync(key, cancellation);
            }
        }
    }

    /// <summary>
    /// One more scanner on the running count that outlives the scanner records (they expire after ninety days):
    /// an increment on the summary document, or, the first time, the summary written with every scanner kept.
    /// </summary>
    private async Task CountScannerAsync(PartitionKey key, CancellationToken cancellation)
    {
        try
        {
            await _container.PatchItemAsync<VisitorSummary>(VisitorSummary.Name, key, [PatchOperation.Increment("/scanners_ever", 1)], cancellationToken: cancellation);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            int scanners = (await CountByKindAsync(cancellation)).GetValueOrDefault(VisitorKinds.Word(VisitorKind.Scanners));
            try
            {
                await _container.CreateItemAsync(new VisitorSummary { ScannersEver = scanners }, key, cancellationToken: cancellation);
            }
            catch (CosmosException conflict) when (conflict.StatusCode == HttpStatusCode.Conflict)
            {
                // The other site wrote it first, with this scanner in its count.
            }
        }
    }

    /// <summary>How many visitor records there are of each kind, by its word: one aggregate query, no document moved.</summary>
    private async Task<Dictionary<string, int>> CountByKindAsync(CancellationToken cancellation)
    {
        var query = new QueryDefinition("SELECT c.who AS who, COUNT(1) AS n FROM c WHERE c.day = @day AND c.kind = @kind GROUP BY c.who")
            .WithParameter("@day", Partition)
            .WithParameter("@kind", VisitorDocument.KindName);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var feed = _container.GetItemQueryIterator<KindCount>(query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(Partition) });
        while (feed.HasMoreResults)
        {
            foreach (var row in await feed.ReadNextAsync(cancellation))
            {
                counts[row.Who] = counts.GetValueOrDefault(row.Who) + row.N;
            }
        }
        return counts;
    }

    /// <summary>One row of the count by kind.</summary>
    private sealed class KindCount
    {
        public string Who { get; set; } = "";

        public int N { get; set; }
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
        var held = _tally;
        if (held is not null && now - held.At <= TallyAge && held.Since == since)
        {
            return held.Tally;
        }
        VisitorTally tally;
        try
        {
            tally = await AggregateTallyAsync(since, cancellation);
        }
        catch (CosmosException)
        {
            // The aggregate read is new; if the store refuses it, the whole partition read it replaced still answers.
            tally = await WholeTallyAsync(since, cancellation);
        }
        _tally = new TallyHeld(now, since, tally);
        return tally;
    }

    /// <summary>
    /// The tally from counts, not documents (ADR-116, the addendum on the tally): the records by kind, the first
    /// person's first day, and the first days of the people first seen since the day asked, which is the thirty-day
    /// window. The scanners left out are the running count on the summary, which outlives the records' ninety days.
    /// </summary>
    private async Task<VisitorTally> AggregateTallyAsync(string since, CancellationToken cancellation)
    {
        var byKind = await CountByKindAsync(cancellation);
        string person = VisitorKinds.Word(VisitorKind.People);
        var partition = new QueryRequestOptions { PartitionKey = new PartitionKey(Partition) };
        string? first = null;
        var firstQuery = new QueryDefinition("SELECT VALUE MIN(c.first_seen) FROM c WHERE c.day = @day AND c.kind = @kind AND c.who = @who")
            .WithParameter("@day", Partition).WithParameter("@kind", VisitorDocument.KindName).WithParameter("@who", person);
        using (var feed = _container.GetItemQueryIterator<string?>(firstQuery, requestOptions: partition))
        {
            while (feed.HasMoreResults)
            {
                first = (await feed.ReadNextAsync(cancellation)).FirstOrDefault(day => !string.IsNullOrEmpty(day)) ?? first;
            }
        }
        var recent = new Dictionary<string, int>(StringComparer.Ordinal);
        var recentQuery = new QueryDefinition("SELECT VALUE c.first_seen FROM c WHERE c.day = @day AND c.kind = @kind AND c.who = @who AND c.first_seen >= @since")
            .WithParameter("@day", Partition).WithParameter("@kind", VisitorDocument.KindName).WithParameter("@who", person).WithParameter("@since", since);
        using (var feed = _container.GetItemQueryIterator<string>(recentQuery, requestOptions: partition))
        {
            while (feed.HasMoreResults)
            {
                foreach (string day in await feed.ReadNextAsync(cancellation))
                {
                    recent[day] = recent.GetValueOrDefault(day) + 1;
                }
            }
        }
        int scanners = byKind.GetValueOrDefault(VisitorKinds.Word(VisitorKind.Scanners));
        try
        {
            var summary = (await _container.ReadItemAsync<VisitorSummary>(VisitorSummary.Name, new PartitionKey(Partition), cancellationToken: cancellation)).Resource;
            scanners = Math.Max(scanners, summary.ScannersEver);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // No scanner has been counted onto the summary yet: the records are the whole count.
        }
        int people = byKind.GetValueOrDefault(person);
        return new VisitorTally(people, scanners, people == 0 ? null : first, recent);
    }

    /// <summary>The tally read from every visitor document, as it was before the aggregate read.</summary>
    private async Task<VisitorTally> WholeTallyAsync(string since, CancellationToken cancellation)
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
        var people = visitors.Where(v => VisitorKinds.FromWord(v.Who) == VisitorKind.People).ToList();
        return new VisitorTally(
            people.Count,
            visitors.Count(v => VisitorKinds.FromWord(v.Who) == VisitorKind.Scanners),
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

    /// <summary>
    /// How long the record is kept, in seconds (<c>ttl</c>): a scanner ninety days, a person and the site never
    /// (-1). The store applies it when the container keeps a time to live, and ignores it when it keeps none.
    /// </summary>
    public int Ttl { get; set; } = -1;

    /// <summary>The time to live a record of this kind is written with.</summary>
    public static int TtlFor(VisitorKind kind) => kind == VisitorKind.Scanners ? ScannerDays * 86_400 : -1;

    /// <summary>How many days a scanner's record is kept.</summary>
    public const int ScannerDays = 90;
}
// #endregion cosmos-visitor-store
