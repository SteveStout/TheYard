// Reads a Cost off each response type the SDK returns. A transactional batch's response is not
// the SDK's Response<T> that items and containers answer with, so each kind gets its own line.
using Microsoft.Azure.Cosmos;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>Reads the request charge and status code off each response type the SDK returns.</summary>
public static class Costs
{
    /// <summary>The charge and status of an item or container response.</summary>
    public static Cost Cost<T>(this Response<T> response) => new(response.RequestCharge, (int)response.StatusCode);

    /// <summary>The charge and status of a transactional batch, which is not a <c>Response&lt;T&gt;</c>.</summary>
    public static Cost Cost(this TransactionalBatchResponse response) => new(response.RequestCharge, (int)response.StatusCode);
}
