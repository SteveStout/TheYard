using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// The nightly card's run log in the document store (ADR: The Nightly Function on the Admin tab):
/// one container partitioned on the UTC day, a document per GitHub run under the run's own id and
/// a document per judged slot under its hour, each upserted so a second read of the same run or
/// slot replaces the first; the container's time to live keeps a year.
/// </summary>
public sealed class CosmosNightlyRunLog(CosmosStore store) : INightlyRunLog
{
    private readonly Container _container = store.ContainerNamed(Containers.NightlyRuns);
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
                : new PlanHistoryAvailability(false, $"the {Containers.NightlyRuns} container is partitioned on {response.Resource.PartitionKeyPath} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new PlanHistoryAvailability(false, $"the {Containers.NightlyRuns} container is not there yet; apply infra/cosmos/{Containers.NightlyRuns}.json");
        }
        catch (Exception ex)
        {
            return _availability = new PlanHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    public async Task UpsertAsync(IReadOnlyList<NightlyRun> runs, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }
        foreach (var run in runs)
        {
            var document = new RunDocument
            {
                Id = run.Id,
                Day = PlanFolding.DayOf(run.At),
                At = run.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Run = run,
            };
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }

    public async Task UpsertFiringsAsync(IReadOnlyList<NightlyFiring> firings, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }
        foreach (var firing in firings)
        {
            var document = new FiringDocument
            {
                Id = "firing:" + PlanFolding.HourKey(firing.Slot),
                Day = PlanFolding.DayOf(firing.Slot),
                At = firing.Slot.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Firing = firing,
            };
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }

    public async Task<IReadOnlyList<NightlyRun>> SinceAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var definition = new QueryDefinition("SELECT * FROM c WHERE c.kind = 'run' AND c.day >= @day AND c.at >= @at")
            .WithParameter("@day", PlanFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var runs = new List<NightlyRun>();
        using var feed = _container.GetItemQueryIterator<RunDocument>(definition);
        while (feed.HasMoreResults)
        {
            runs.AddRange((await feed.ReadNextAsync(cancellation)).Select(document => document.Run));
        }
        return runs;
    }

    /// <summary>One GitHub run of the nightly workflow, under GitHub's id.</summary>
    private sealed class RunDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "run";
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("run")] public NightlyRun Run { get; set; } = null!;
    }

    /// <summary>One slot of the Function, judged.</summary>
    private sealed class FiringDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "firing";
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("firing")] public NightlyFiring Firing { get; set; } = null!;
    }
}
