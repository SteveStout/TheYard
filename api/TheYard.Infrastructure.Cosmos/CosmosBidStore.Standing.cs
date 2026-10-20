// The standing half of the document bid store: each vehicle's standing as one document in the bids
// container's standing partition, moved with a compare-and-set on its etag. A file of its own
// because CosmosSources.cs holds the three adapters and the buyer documents, and this is the part
// two sites over one account meet in.
using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>The standing documents, read and moved; the adapter itself is described in CosmosSources.cs.</summary>
public sealed partial class CosmosBidStore
{
    private const string StandingLabel = "the standing partition";

    // #region cosmos-standing-store
    /// <summary>Every standing: one query inside the standing partition.</summary>
    public async Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync()
    {
        var documents = await store.QueryAsync<StandingDocument>(
            store.Bids, new QueryDefinition("SELECT * FROM c"), StandingDocument.Partition, StandingLabel);
        return documents.Select(Stored).ToList();
    }

    /// <summary>One vehicle's standing: a point read, null when there is none.</summary>
    public async Task<StoredStanding?> ReadStandingAsync(string vehicleId)
    {
        var document = await store.ReadAsync<StandingDocument>(store.Bids, vehicleId, StandingDocument.Partition, StandingLabel);
        return document is null ? null : Stored(document);
    }

    /// <summary>
    /// The compare-and-set: a create when the caller expected no standing,
    /// which a second creator loses with 409, and a replace carrying the etag
    /// the caller read otherwise, which a stale etag loses with 412. A replace
    /// of a standing another process has since reset loses with 404, the same
    /// loss by another route (the relational store updates no row and answers
    /// false). Every loss answers false, and the service reads again.
    /// </summary>
    private async Task<bool> TryWriteStandingAsync(string vehicleId, VehicleStanding standing, string? expectedVersion)
    {
        var document = new StandingDocument
        {
            Id = vehicleId,
            Amount = standing.Amount,
            BidCount = standing.BidCount,
            HighBidderId = standing.HighBidderId,
            SoldBuyNow = standing.SoldBuyNow,
            AtMs = standing.AtMs,
        };
        try
        {
            if (expectedVersion is null)
            {
                await store.CreateAsync(store.Bids, document, StandingDocument.Partition, StandingLabel);
            }
            else
            {
                await store.ReplaceAsync(store.Bids, document, vehicleId, StandingDocument.Partition, expectedVersion, StandingLabel);
            }
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes the standing of each vehicle, one point delete apiece. A
    /// standing already gone is the answer wanted, so a 404 is not an error.
    /// </summary>
    private async Task ForgetStandingsAsync(IReadOnlyList<string> vehicleIds)
    {
        foreach (string vehicleId in vehicleIds)
        {
            try
            {
                await store.DeleteAsync<StandingDocument>(store.Bids, vehicleId, StandingDocument.Partition, StandingLabel);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Deliberately empty: nothing to forget.
            }
        }
    }

    /// <summary>A document as the port hands it back, the etag as the version.</summary>
    private static StoredStanding Stored(StandingDocument d) => new(
        d.Id,
        new VehicleStanding(d.Amount, d.BidCount, d.HighBidderId, d.SoldBuyNow, d.AtMs),
        d.ETag ?? "");
    // #endregion cosmos-standing-store
}
