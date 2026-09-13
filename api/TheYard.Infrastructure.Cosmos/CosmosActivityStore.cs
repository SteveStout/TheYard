using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// Site activity kept in the document store (ADR: Site activity, and the line
/// an address does not cross). One container partitioned on the UTC day, two
/// kinds of document in it, both counters.
///
/// <para>The counters move by partial update: a batch's delta becomes an
/// Increment on the document's own fields, applied by the service, so two
/// containers adding to the same hour add. The paths object is read, merged
/// and set back, which is the one field a batch can lose to a batch from the
/// other container in the same five seconds, the same trade the relational
/// side makes and for the same reason.</para>
///
/// <para>This adapter calls the container directly rather than through the
/// measured helpers in <see cref="CosmosStore"/>, on purpose: a batch every
/// five seconds would fill the Admin tab's two-hundred-slot store log with the
/// feature that reads the store log, the observer effect the request ring
/// already had to design out (ADR: What the store is actually doing). What it
/// costs is counted here instead and reported with the card.</para>
/// </summary>
public sealed class CosmosActivityStore(CosmosStore store) : IActivityStore
{
    private readonly Container _container = store.ContainerNamed(Containers.Activity);
    private ActivityAvailability? _availability;
    private double _charge;
    private int _operations;
    private int _failures;

    /// <summary>What this feature has cost the store since the process started: request units, operations, failures.</summary>
    public (double Charge, int Operations, int Failures) Cost => (Math.Round(_charge, 2), _operations, _failures);

    // #region availability
    public async Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }

        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            Count(response.RequestCharge);
            string key = response.Resource.PartitionKeyPath;
            return _availability = key == Containers.PartitionKeyPaths[Containers.Activity]
                ? new ActivityAvailability(true, "kept in Azure Cosmos DB")
                : new ActivityAvailability(false, $"the activity container is partitioned on {key} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new ActivityAvailability(false, "the activity container is not there; apply infra/cosmos/activity.json");
        }
        catch (Exception ex)
        {
            return _availability = new ActivityAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }
    // #endregion availability

    // #region record
    public async Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation)
    {
        if (hits.Count == 0 || !(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }

        foreach (var delta in ActivityFolding.Hours(hits))
        {
            string day = ActivityFolding.DayOf(delta.Hour);
            string id = ActivityHourDocument.IdFor(delta.Store, delta.Hour);
            await MoveAsync(
                id,
                day,
                () => new ActivityHourDocument
                {
                    Id = id,
                    Day = day,
                    Store = delta.Store,
                    Hour = delta.Hour.ToUniversalTime().ToString("O"),
                    Requests = delta.Requests,
                    Bots = delta.Bots,
                    Paths = new Dictionary<string, int>(ActivityFolding.Merge(Empty, delta.Paths), StringComparer.Ordinal),
                },
                (ActivityHourDocument stored) =>
                [
                    PatchOperation.Increment("/requests", delta.Requests),
                    PatchOperation.Increment("/bots", delta.Bots),
                    PatchOperation.Set("/paths", ActivityFolding.Merge(stored.Paths, delta.Paths)),
                ],
                cancellation);
        }

        foreach (var delta in ActivityFolding.Visitors(hits))
        {
            string id = ActivityVisitorDocument.IdFor(delta.Store, delta.Visitor);
            await MoveAsync(
                id,
                delta.Day,
                () => new ActivityVisitorDocument
                {
                    Id = id,
                    Day = delta.Day,
                    Store = delta.Store,
                    Visitor = delta.Visitor,
                    Network = delta.Network,
                    FirstSeen = delta.First,
                    LastSeen = delta.Last,
                    Requests = delta.Requests,
                    Bots = delta.Bots,
                    Paths = new Dictionary<string, int>(ActivityFolding.Merge(Empty, delta.Paths), StringComparer.Ordinal),
                },
                (ActivityVisitorDocument stored) =>
                {
                    var operations = new List<PatchOperation>
                    {
                        PatchOperation.Increment("/requests", delta.Requests),
                        PatchOperation.Increment("/bots", delta.Bots),
                        PatchOperation.Set("/paths", ActivityFolding.Merge(stored.Paths, delta.Paths)),
                    };
                    if (delta.Last > stored.LastSeen)
                    {
                        operations.Add(PatchOperation.Set("/last_seen", delta.Last));
                    }
                    if (delta.First < stored.FirstSeen)
                    {
                        operations.Add(PatchOperation.Set("/first_seen", delta.First));
                    }
                    return operations;
                },
                cancellation);
        }
    }

    /// <summary>
    /// Read the document, patch it with what the batch adds, or create it if
    /// there was nothing. Two tries: a 409 on the create means the other
    /// container created it between the read and the write, and the second
    /// try reads what it wrote and patches that; a 404 on the patch is the
    /// mirror case and gets the create. A failure after that is counted and
    /// dropped, because a count of visitors is not worth a request thread and
    /// this is never on one anyway.
    /// </summary>
    private async Task MoveAsync<TDocument>(
        string id,
        string day,
        Func<TDocument> fresh,
        Func<TDocument, IReadOnlyList<PatchOperation>> patch,
        CancellationToken cancellation) where TDocument : class
    {
        var partition = new PartitionKey(day);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                TDocument? stored;
                try
                {
                    var read = await _container.ReadItemAsync<TDocument>(id, partition, cancellationToken: cancellation);
                    Count(read.RequestCharge);
                    stored = read.Resource;
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    Count(ex.RequestCharge);
                    stored = null;
                }

                if (stored is null)
                {
                    var created = await _container.CreateItemAsync(fresh(), partition, cancellationToken: cancellation);
                    Count(created.RequestCharge);
                    return;
                }

                var patched = await _container.PatchItemAsync<TDocument>(id, partition, patch(stored), cancellationToken: cancellation);
                Count(patched.RequestCharge);
                return;
            }
            catch (CosmosException ex) when (attempt == 1 && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                Count(ex.RequestCharge);
            }
            catch (CosmosException ex)
            {
                Count(ex.RequestCharge);
                _failures++;
                return;
            }
        }
    }
    // #endregion record

    // #region read
    public async Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day AND c.hour >= @hour")
            .WithParameter("@kind", ActivityHourDocument.KindName)
            .WithParameter("@day", ActivityFolding.DayOf(since))
            .WithParameter("@hour", ActivityFolding.HourOf(since).ToString("O"));
        var documents = await ReadAllAsync<ActivityHourDocument>(query, cancellation);
        return documents
            .Select(d => new ActivityHour(d.Store, DateTimeOffset.Parse(d.Hour, null, System.Globalization.DateTimeStyles.RoundtripKind), d.Requests, d.Bots, d.Paths))
            .OrderBy(hour => hour.Store, StringComparer.Ordinal)
            .ThenBy(hour => hour.Hour)
            .ToList();
    }

    public async Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day")
            .WithParameter("@kind", ActivityVisitorDocument.KindName)
            .WithParameter("@day", ActivityFolding.DayOf(since));
        var documents = await ReadAllAsync<ActivityVisitorDocument>(query, cancellation);
        return documents
            .Select(d => new ActivityVisitor(d.Store, d.Day, d.Visitor, d.Network, d.FirstSeen, d.LastSeen, d.Requests, d.Bots, d.Paths))
            .OrderByDescending(visitor => visitor.LastSeen)
            .ToList();
    }

    private async Task<List<TDocument>> ReadAllAsync<TDocument>(QueryDefinition query, CancellationToken cancellation)
    {
        var items = new List<TDocument>();
        using var feed = _container.GetItemQueryIterator<TDocument>(query);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellation);
            Count(page.RequestCharge);
            items.AddRange(page);
        }

        return items;
    }
    // #endregion read

    private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>(StringComparer.Ordinal);

    private void Count(double charge)
    {
        _charge += charge;
        _operations++;
    }
}
