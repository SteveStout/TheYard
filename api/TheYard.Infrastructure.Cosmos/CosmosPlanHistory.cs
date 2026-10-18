using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// The App Service plan, kept in the document store (ADR: The App Service plan, read every
/// minute). One container partitioned on the UTC day, a minute document every minute and an hour
/// document every hour, and the container's own time-to-live as the retention. Like the machines
/// adapter it calls the container directly, so a write a minute never shows in the store log.
/// </summary>
public sealed class CosmosPlanHistory(CosmosStore store) : IPlanHistory
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
            string key = response.Resource.PartitionKeyPath;
            int? ttl = response.Resource.DefaultTimeToLive;
            return _availability = key == Containers.PartitionKeyPaths[Containers.Plan]
                ? new PlanHistoryAvailability(true, ttl is > 0 ? $"kept in Azure Cosmos DB for {ttl.Value / 86_400} days" : "kept in Azure Cosmos DB with no expiry")
                : new PlanHistoryAvailability(false, $"the plan container is partitioned on {key} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new PlanHistoryAvailability(false, "the plan container is not there yet; apply infra/cosmos/plan.json");
        }
        catch (Exception ex)
        {
            return _availability = new PlanHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    // #region plan-append
    public async Task AppendMinuteAsync(PlanMinute minute, CancellationToken cancellation)
    {
        if ((await AvailabilityAsync(cancellation)).Available)
        {
            var document = PlanMinuteDocument.From(minute);
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }

    public async Task AppendHourAsync(PlanHour hour, CancellationToken cancellation)
    {
        if ((await AvailabilityAsync(cancellation)).Available)
        {
            var document = PlanHourDocument.From(hour);
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }
    // #endregion plan-append

    public async Task<IReadOnlyList<PlanMinute>> NewestAsync(int count, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        // Today's partition and yesterday's, so the hour after midnight still reads a full hour.
        var now = DateTimeOffset.UtcNow;
        var definition = new QueryDefinition("SELECT TOP @count * FROM c WHERE c.kind = 'minute' AND (c.day = @today OR c.day = @yesterday) ORDER BY c.at DESC")
            .WithParameter("@count", count)
            .WithParameter("@today", PlanFolding.DayOf(now))
            .WithParameter("@yesterday", PlanFolding.DayOf(now.AddDays(-1)));
        var documents = await ReadAll<PlanMinuteDocument>(definition, cancellation);
        return documents.Select(document => document.ToMinute()).OrderBy(minute => minute.At).ToList();
    }

    // #region plan-buckets
    public async Task<IReadOnlyList<PlanBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        // The bucket key comes from an enum and never from a request; the bounds are parameters.
        string key = grain switch
        {
            MachineGrain.FiveMinutes => "b5",
            MachineGrain.Hour => "h1",
            _ => "h4",
        };
        var definition = new QueryDefinition(
                $"SELECT c.{key} AS bucket, COUNT(1) AS minutes, AVG(c.cpu) AS cpu, MAX(c.cpu) AS cpu_max, AVG(c.memory) AS memory, "
                + $"MAX(c.memory) AS memory_max, MAX(c.http_queue) AS http_queue, MAX(c.version) AS version "
                + $"FROM c WHERE c.kind = 'minute' AND c.day >= @day AND c.at >= @at GROUP BY c.{key}")
            .WithParameter("@day", PlanFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var rows = await ReadAll<BucketRow>(definition, cancellation);
        return rows
            .Where(row => !string.IsNullOrEmpty(row.Bucket))
            .OrderBy(row => row.Bucket, StringComparer.Ordinal)
            .Select(row => new PlanBucket(
                DateTimeOffset.Parse(row.Bucket + ":00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                (int)Math.Round(row.Minutes),
                Rounded(row.Cpu),
                Rounded(row.CpuMax),
                Rounded(row.Memory),
                Rounded(row.MemoryMax),
                Rounded(row.HttpQueue),
                row.Version))
            .ToList();
    }
    // #endregion plan-buckets

    public async Task<PlanSpread> SpreadAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        // The five-minute means are a few thousand rows for a month, which the store groups; the
        // percentile is taken here over them, since the store has no percentile function.
        var fives = await BucketsAsync(since, MachineGrain.FiveMinutes, cancellation);
        return new PlanSpread(
            fives.Sum(bucket => bucket.Minutes),
            PlanFolding.P95(fives.Select(bucket => bucket.CpuPercent)),
            MachineFolding.MaxOf(fives.Select(bucket => bucket.CpuMaxPercent)),
            PlanFolding.P95(fives.Select(bucket => bucket.MemoryPercent)),
            MachineFolding.MaxOf(fives.Select(bucket => bucket.MemoryMaxPercent)));
    }

    public async Task<IReadOnlyList<PlanHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var definition = new QueryDefinition("SELECT * FROM c WHERE c.kind = 'hour' AND c.day >= @day AND c.at >= @at")
            .WithParameter("@day", PlanFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var documents = await ReadAll<PlanHourDocument>(definition, cancellation);
        return documents.Select(document => document.Hour).OrderBy(hour => hour.At).ToList();
    }

    private async Task<List<T>> ReadAll<T>(QueryDefinition definition, CancellationToken cancellation)
    {
        var rows = new List<T>();
        using var feed = _container.GetItemQueryIterator<T>(definition);
        while (feed.HasMoreResults)
        {
            rows.AddRange(await feed.ReadNextAsync(cancellation));
        }
        return rows;
    }

    private static double? Rounded(double? value) => value is null ? null : Math.Round(value.Value, 2);

    /// <summary>One group as the store answers it; an average over minutes that all lack a figure arrives as null.</summary>
    private sealed class BucketRow
    {
        public string Bucket { get; set; } = "";
        public double Minutes { get; set; }
        public double? Cpu { get; set; }
        public double? CpuMax { get; set; }
        public double? Memory { get; set; }
        public double? MemoryMax { get; set; }
        public double? HttpQueue { get; set; }
        public string? Version { get; set; }
    }
}
