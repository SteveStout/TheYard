using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// Every Claude lane's usage in the document store (ADR: Every Claude lane reports its usage): in
/// the plan container, partitioned on the UTC day, one document per run under its lane and run id,
/// upserted so a document posted twice is kept once. Its kind is Containers.UsageKind, so the plan's
/// own minutes never meet it, and each document keeps itself a year, longer than the plan's month.
/// </summary>
public sealed class CosmosUsageLog(CosmosStore store) : IUsageLog
{
    private readonly Container _container = store.ContainerNamed(Containers.Plan);
    private PlanHistoryAvailability? _availability;

    public async Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }
        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            return _availability = response.Resource.PartitionKeyPath == "/day"
                ? new PlanHistoryAvailability(true, "kept in Cosmos DB")
                : new PlanHistoryAvailability(false, $"the {Containers.Plan} container is partitioned on {response.Resource.PartitionKeyPath} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new PlanHistoryAvailability(false, $"the {Containers.Plan} container is not there yet; apply infra/cosmos/{Containers.Plan}.json");
        }
        catch (Exception ex)
        {
            return _availability = new PlanHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    public async Task UpsertAsync(UsageRecord record, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }
        var document = new UsageDocument
        {
            // The lane may carry spaces and commas; an id may not carry a slash, and neither may.
            Id = Containers.UsageKind + ":" + Uri.EscapeDataString(MemoryUsageLog.KeyOf(record)),
            Day = record.Day,
            At = record.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Record = record,
        };
        await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
    }

    public async Task<IReadOnlyList<UsageRecord>> SinceAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var definition = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day AND c.at >= @at")
            .WithParameter("@kind", Containers.UsageKind)
            .WithParameter("@day", since.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var records = new List<UsageRecord>();
        using var feed = _container.GetItemQueryIterator<UsageDocument>(definition);
        while (feed.HasMoreResults)
        {
            records.AddRange((await feed.ReadNextAsync(cancellation)).Select(document => document.Record));
        }
        return records;
    }

    /// <summary>One lane's run of Claude.</summary>
    private sealed class UsageDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = Containers.UsageKind;
        [JsonPropertyName("ttl")] public int Ttl { get; set; } = Containers.NightlyRunSeconds;
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("record")] public UsageRecord Record { get; set; } = null!;
    }
}
