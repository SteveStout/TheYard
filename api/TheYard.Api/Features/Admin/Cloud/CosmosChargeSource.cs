// The Cosmos DB card's live source (ADR: Live dials where the source allows, the addendum on Cosmos DB's line): this
// process's own requests to the account, their charges summed every five seconds from the operations ring the Machines
// card already keeps. Cosmos DB has no fifteen-second view of the whole account, so the dial stays Azure Monitor's
// minute and this line says only what this process asked for.
using TheYard.Application.Shared.Live;

namespace TheYard.Api.Features.Admin.Cloud;

// #region cosmos-charge-source
/// <summary>This process's request units a second against the document store, every five seconds while leased.</summary>
/// <param name="operations">The operations ring, oldest first or in any order.</param>
/// <param name="hasStore">Whether this process keeps a document store at all.</param>
public sealed class CosmosChargeSource(Func<IReadOnlyList<StoreOperation>> operations, Func<bool> hasStore) : ILiveSource
{
    /// <summary>The window each sample sums.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public string Key => "cosmosdb";

    /// <inheritdoc />
    public int GrainSeconds => 5;

    /// <inheritdoc />
    public string Source => "this process's requests, live";

    /// <inheritdoc />
    public string? Absent => hasStore() ? null : "this site keeps no document store";

    /// <summary>The last five seconds' charges as request units a second, and how many requests made them.</summary>
    public Task<LiveSample?> SampleAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        var recent = operations().Where(operation => operation.At > now - Window && operation.At <= now).ToList();
        return Task.FromResult<LiveSample?>(new LiveSample(now, new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["ru_per_second"] = Math.Round(recent.Sum(operation => operation.RequestCharge) / Window.TotalSeconds, 2),
            ["requests"] = recent.Count,
            ["throttled"] = recent.Count(operation => operation.Outcome.Contains("TooManyRequests", StringComparison.Ordinal)),
        }));
    }
}
// #endregion cosmos-charge-source
