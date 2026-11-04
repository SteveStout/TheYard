using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using TheYard.Application.Features.Review;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Features.Review;

/// <summary>
/// The review route's reach in the document store (ADR: Ten minutes, seven stops, the addendum on the reach
/// rows): one document a visitor a day in the activity container, the fourth kind beside the hours, the
/// visitors and the link opens, partitioned on the day like the opens, with no expiry. A write reads the
/// day's row and folds through <see cref="ReviewFolding"/>, then replaces it only if nobody wrote it since
/// (the row's ETag), so a lower stop from a second container never lowers a row the first raised.
/// </summary>
public sealed class CosmosReviewStore(CosmosStore store) : IReviewStore
{
    private readonly Container _container = store.ContainerNamed(Containers.Activity);
    private bool? _available;
    private long _notKept;

    /// <summary>Where the reach is kept, for the card.</summary>
    public string Source => "kept in Azure Cosmos DB beside the site's activity, with no expiry";

    /// <summary>Whether the activity container is there and partitioned on the day; a yes is kept, a no asked again.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<bool> AvailableAsync(CancellationToken cancellation)
    {
        if (_available is true)
        {
            return true;
        }
        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            return (_available = response.Resource.PartitionKeyPath == Containers.PartitionKeyPaths[Containers.Activity]).Value;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    /// <summary>Folds each row into the one kept for its visitor and day, by the one rule, two tries each.</summary>
    /// <param name="rows">The rows the collector folded.</param>
    /// <param name="cancellation">The write's cancellation.</param>
    public async Task KeepAsync(IReadOnlyList<ReviewRow> rows, CancellationToken cancellation)
    {
        if (!await AvailableAsync(cancellation))
        {
            return;
        }
        foreach (var row in rows)
        {
            string id = ReviewDocument.IdFor(row.Day, row.Hash);
            var partition = new PartitionKey(row.Day);
            // Two tries: a 409 on the create or a 412 on the replace means another container wrote the
            // row between the read and the write, and the second try folds into what it wrote.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    ItemResponse<ReviewDocument>? stored;
                    try
                    {
                        stored = await _container.ReadItemAsync<ReviewDocument>(id, partition, cancellationToken: cancellation);
                    }
                    catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        stored = null;
                    }
                    if (stored is null)
                    {
                        await _container.CreateItemAsync(DocumentFor(row), partition, cancellationToken: cancellation);
                        break;
                    }
                    var merged = ReviewFolding.Merge(RowOf(stored.Resource), row);
                    await _container.ReplaceItemAsync(DocumentFor(merged), id, partition, new ItemRequestOptions { IfMatchEtag = stored.ETag }, cancellation);
                    break;
                }
                catch (CosmosException ex) when (attempt == 1 && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                {
                    // Try once more, folding into what the other writer kept.
                }
                catch (CosmosException ex)
                {
                    // A reach is not worth a retry loop and this is never on a request thread: the row is
                    // dropped, said by its type and status, never its message.
                    long notKept = Interlocked.Increment(ref _notKept);
                    store.Recorder.Logger.LogWarning("A review-reach row was not kept: {Type} {Status}, {NotKept} since start", ex.GetType().Name, (int)ex.StatusCode, notKept);
                    break;
                }
            }
        }
    }

    /// <summary>Every reach document from a day on.</summary>
    /// <param name="day">The first day, yyyy-MM-dd.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<IReadOnlyList<ReviewRow>> RowsSinceAsync(string day, CancellationToken cancellation)
    {
        if (!await AvailableAsync(cancellation))
        {
            return [];
        }
        var query = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day")
            .WithParameter("@kind", ReviewDocument.KindName)
            .WithParameter("@day", day);
        var rows = new List<ReviewRow>();
        using var feed = _container.GetItemQueryIterator<ReviewDocument>(query);
        while (feed.HasMoreResults)
        {
            foreach (var document in await feed.ReadNextAsync(cancellation))
            {
                rows.Add(RowOf(document));
            }
        }
        return [.. rows.OrderBy(row => row.Day, StringComparer.Ordinal).ThenBy(row => row.Hash, StringComparer.Ordinal)];
    }

    /// <summary>The earliest day a reach document is kept, one aggregate read across the days.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string?> FirstDayAsync(CancellationToken cancellation)
    {
        if (!await AvailableAsync(cancellation))
        {
            return null;
        }
        var query = new QueryDefinition("SELECT VALUE MIN(c.day) FROM c WHERE c.kind = @kind")
            .WithParameter("@kind", ReviewDocument.KindName);
        using var feed = _container.GetItemQueryIterator<string?>(query);
        string? first = null;
        while (feed.HasMoreResults)
        {
            foreach (var day in await feed.ReadNextAsync(cancellation))
            {
                if (day is not null && (first is null || string.CompareOrdinal(day, first) < 0))
                {
                    first = day;
                }
            }
        }
        return first;
    }

    /// <summary>A row as the document that keeps it.</summary>
    /// <param name="row">The row.</param>
    public static ReviewDocument DocumentFor(ReviewRow row) => new()
    {
        Id = ReviewDocument.IdFor(row.Day, row.Hash),
        Day = row.Day,
        Hash = row.Hash,
        Stop = row.Stop,
        Code = row.Code,
        FirstSeen = row.First,
        LastSeen = row.Last,
    };

    /// <summary>A kept document as a row.</summary>
    /// <param name="document">The document as stored.</param>
    public static ReviewRow RowOf(ReviewDocument document) =>
        new(document.Hash, document.Day, document.Stop, document.Code, document.FirstSeen, document.LastSeen);
}
