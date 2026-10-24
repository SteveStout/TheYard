using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using TheYard.Application.Features.Links;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Features.Links;

/// <summary>
/// Tagged link opens in the document store (ADR: Tagged links, who opened what): one document a
/// code a day in the activity container, the third kind beside the hours and the visitors, moved
/// in place by partial updates the way the visitors are, so two containers opening the same code
/// add rather than overwrite. The activity container is used because it already exists on the
/// account with no expiry and a partition on the day, so a link is kept as long as the site's
/// activity is and nothing new has to be applied to the account. Every count moves by an atomic
/// increment, the opens and each path's alike, so two writers in the same second both land.
/// </summary>
public sealed class CosmosLinkOpens(CosmosStore store) : ILinkOpens
{
    /// <summary>The most operations Cosmos DB takes in one partial update.</summary>
    public const int OperationsPerPatch = 10;

    private readonly Container _container = store.ContainerNamed(Containers.Activity);
    private LinkOpensAvailability? _availability;
    private long _notKept;

    public async Task<LinkOpensAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }
        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            string key = response.Resource.PartitionKeyPath;
            return _availability = key == Containers.PartitionKeyPaths[Containers.Activity]
                ? new LinkOpensAvailability(true, "kept in Azure Cosmos DB beside the site's activity, with no expiry")
                : new LinkOpensAvailability(false, $"the activity container is partitioned on {key} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new LinkOpensAvailability(false, "the activity container is not there; apply infra/cosmos/activity.json");
        }
        catch (Exception ex)
        {
            return _availability = new LinkOpensAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    public async Task RecordAsync(IReadOnlyList<LinkOpen> opens, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }
        foreach (var delta in LinkOpenFolding.Fold(opens))
        {
            string id = LinkOpenDocument.IdFor(delta.Code);
            var partition = new PartitionKey(delta.Day);
            // Two tries, the way the activity store moves its rows: a 409 on the create means the
            // other container created the document between the read and the write, and the second
            // try patches what it wrote; a 404 on the patch is the mirror case.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    LinkOpenDocument? stored;
                    try
                    {
                        stored = (await _container.ReadItemAsync<LinkOpenDocument>(id, partition, cancellationToken: cancellation)).Resource;
                    }
                    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        stored = null;
                    }
                    if (stored is null)
                    {
                        await _container.CreateItemAsync(
                            new LinkOpenDocument
                            {
                                Id = id,
                                Day = delta.Day,
                                Code = delta.Code,
                                FirstSeen = delta.First,
                                LastSeen = delta.Last,
                                Opens = delta.Opens,
                                Paths = new Dictionary<string, int>(delta.Paths, StringComparer.Ordinal),
                            },
                            partition,
                            cancellationToken: cancellation);
                        break;
                    }
                    foreach (var patch in PatchesFor(stored, delta))
                    {
                        await _container.PatchItemAsync<LinkOpenDocument>(id, partition, patch, cancellationToken: cancellation);
                    }
                    break;
                }
                catch (CosmosException ex) when (attempt == 1 && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                {
                    // Try once more.
                }
                catch (CosmosException ex)
                {
                    // A count of opens is not worth a retry loop, and this is never on a request
                    // thread: this row is dropped, said by its type and status, never its message.
                    long notKept = Interlocked.Increment(ref _notKept);
                    store.Logger.LogWarning("A link-open row was not kept: {Type} {Status}, {NotKept} since start", ex.GetType().Name, (int)ex.StatusCode, notKept);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The partial updates one day's delta makes to a stored row, ten operations at most each: the
    /// opens and every path by increment, a path Cosmos DB does not hold yet created by the same
    /// increment, a new path only while the row holds fewer than the kept number, and the first and
    /// last seen moved only outward.
    /// </summary>
    /// <param name="stored">The row as it was read.</param>
    /// <param name="delta">What the batch adds to it.</param>
    public static IReadOnlyList<IReadOnlyList<PatchOperation>> PatchesFor(LinkOpenDocument stored, LinkOpenDay delta)
    {
        var operations = new List<PatchOperation> { PatchOperation.Increment("/opens", delta.Opens) };
        int room = LinkOpenFolding.PathsKept - stored.Paths.Count;
        foreach (var (path, count) in delta.Paths.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!stored.Paths.ContainsKey(path))
            {
                if (room <= 0)
                {
                    continue;
                }
                room--;
            }
            operations.Add(PatchOperation.Increment("/paths/" + Pointer(path), count));
        }
        if (delta.Last > stored.LastSeen)
        {
            operations.Add(PatchOperation.Set("/last_seen", delta.Last));
        }
        if (delta.First < stored.FirstSeen)
        {
            operations.Add(PatchOperation.Set("/first_seen", delta.First));
        }
        return [.. operations.Chunk(OperationsPerPatch).Select(chunk => (IReadOnlyList<PatchOperation>)chunk)];
    }

    /// <summary>
    /// A stored row as a day, never holding more than the kept number of paths. Two containers that
    /// each read eleven paths can both add a twelfth, so the row can hold thirteen: the patches carry
    /// no precondition (one batch spans chunks a single ETag cannot cover), and the cap is held here,
    /// where every reader meets it.
    /// </summary>
    /// <param name="document">The row as stored.</param>
    public static LinkOpenDay Kept(LinkOpenDocument document) =>
        new(document.Day, document.Code, document.FirstSeen, document.LastSeen, document.Opens, LinkOpenFolding.Top(document.Paths));

    /// <summary>A key as one step of a JSON pointer: "~" as "~0" and "/" as "~1", so "/api/go/github" is one property.</summary>
    /// <param name="key">The dictionary key.</param>
    public static string Pointer(string key) => key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    public async Task<IReadOnlyList<LinkOpenDay>> DaysAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day")
            .WithParameter("@kind", LinkOpenDocument.KindName)
            .WithParameter("@day", since.UtcDateTime.ToString("yyyy-MM-dd"));
        var days = new List<LinkOpenDay>();
        using var feed = _container.GetItemQueryIterator<LinkOpenDocument>(query);
        while (feed.HasMoreResults)
        {
            foreach (var document in await feed.ReadNextAsync(cancellation))
            {
                days.Add(Kept(document));
            }
        }
        return [.. days.OrderBy(day => day.Day, StringComparer.Ordinal).ThenBy(day => day.Code, StringComparer.Ordinal)];
    }
}
