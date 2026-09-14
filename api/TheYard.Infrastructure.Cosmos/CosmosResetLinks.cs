using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// Reset links in the document store (ADR: Accounts and per-user bids,
/// addendum of 14 September). One container partitioned on the id, one
/// document per link, the container's time-to-live as the hour: a document
/// expires on its own and nothing has to run to delete it. Both sites keep
/// theirs here, so a link minted on either site is found on either, and the
/// token inside still names the site it belongs to.
///
/// <para>Called on the container directly rather than through the measured
/// helpers in <see cref="CosmosStore"/>, like the activity and log adapters:
/// a link is minted on the request path and its three operations are a
/// point write, a point read and a point delete, each a few request units.</para>
/// </summary>
public sealed class CosmosResetLinks(CosmosStore store) : IResetLinks
{
    private readonly Container _container = store.ContainerNamed(Containers.Resets);

    public async Task<string> KeepAsync(string token, TimeSpan lifetime, CancellationToken cancellation)
    {
        string id = ResetLinkIds.Fresh();
        var document = new ResetLinkDocument
        {
            Id = id,
            Token = token,
            Expires = DateTimeOffset.UtcNow.Add(lifetime).ToString("O"),
            Ttl = (int)Math.Ceiling(lifetime.TotalSeconds),
        };
        await _container.CreateItemAsync(document, new PartitionKey(id), cancellationToken: cancellation);
        return id;
    }

    public async Task<string?> ReadAsync(string id, CancellationToken cancellation)
    {
        if (!ResetLinkIds.IsOne(id))
        {
            return null;
        }

        try
        {
            var response = await _container.ReadItemAsync<ResetLinkDocument>(id, new PartitionKey(id), cancellationToken: cancellation);
            // The container's clock expires the document; this is the same
            // rule applied a little sooner, so a link read in the seconds
            // before the store sweeps it is still refused.
            return DateTimeOffset.TryParse(response.Resource.Expires, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires) && expires > DateTimeOffset.UtcNow
                ? response.Resource.Token
                : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ForgetAsync(string id, CancellationToken cancellation)
    {
        if (!ResetLinkIds.IsOne(id))
        {
            return false;
        }

        try
        {
            await _container.DeleteItemAsync<ResetLinkDocument>(id, new PartitionKey(id), cancellationToken: cancellation);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
