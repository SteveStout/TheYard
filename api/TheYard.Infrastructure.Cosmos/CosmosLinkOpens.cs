using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Features.Links;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// Tagged link opens in the document store (ADR: Tagged links, who opened what): one document a
/// code a day in the activity container, the third kind beside the hours and the visitors, moved
/// in place by partial updates the way the visitors are, so two containers opening the same code
/// add rather than overwrite. The activity container is used because it already exists on the
/// account with no expiry and a partition on the day, so a link is kept as long as the site's
/// activity is and nothing new has to be applied to the account.
/// </summary>
public sealed class CosmosLinkOpens(CosmosStore store) : ILinkOpens
{
    private readonly Container _container = store.ContainerNamed(Containers.Activity);
    private LinkOpensAvailability? _availability;

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
                    var operations = new List<PatchOperation>
                    {
                        PatchOperation.Increment("/opens", delta.Opens),
                        PatchOperation.Set("/paths", LinkOpenFolding.Merge(stored.Paths, delta.Paths)),
                    };
                    if (delta.Last > stored.LastSeen)
                    {
                        operations.Add(PatchOperation.Set("/last_seen", delta.Last));
                    }
                    if (delta.First < stored.FirstSeen)
                    {
                        operations.Add(PatchOperation.Set("/first_seen", delta.First));
                    }
                    await _container.PatchItemAsync<LinkOpenDocument>(id, partition, operations, cancellationToken: cancellation);
                    break;
                }
                catch (CosmosException ex) when (attempt == 1 && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                {
                    // Try once more.
                }
                catch (CosmosException)
                {
                    // A count of opens is not worth a request thread, and this is never on one; the batch is dropped.
                    break;
                }
            }
        }
    }

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
                days.Add(new LinkOpenDay(document.Day, document.Code, document.FirstSeen, document.LastSeen, document.Opens, document.Paths));
            }
        }
        return [.. days.OrderBy(day => day.Day, StringComparer.Ordinal).ThenBy(day => day.Code, StringComparer.Ordinal)];
    }
}
