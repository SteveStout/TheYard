using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.History;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Features.Admin;

/// <summary>
/// One resource's keep in the document store (ADR: One frame for every resource): a container of
/// its own, partitioned on the UTC day, a minute document a minute and an hour document an hour,
/// expired by the container. The readings sit under one object so each window is one grouped query
/// over the names the card asks for; the names come from code, never from a request. A card that
/// shares another card's container names a kind prefix, and its documents' kinds and ids open with
/// it, so the two never read each other's minutes.
/// </summary>
/// <param name="store">The document store.</param>
/// <param name="containerName">The container the minutes and hours are kept in.</param>
/// <param name="kindPrefix">What this card's kinds and ids open with when it shares a container; empty for a container of its own.</param>
public sealed class CosmosResourceHistory(CosmosStore store, string containerName, string kindPrefix = "") : IResourceHistory
{
    private readonly string _minute = kindPrefix + "minute";
    private readonly string _hour = kindPrefix + "hour";

    private readonly Container _container = store.ContainerNamed(containerName);
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
            int? ttl = response.Resource.DefaultTimeToLive;
            return _availability = response.Resource.PartitionKeyPath == "/day"
                ? new PlanHistoryAvailability(true, ttl is > 0 ? $"kept in Cosmos DB, {ttl.Value / 86_400} days" : "kept in Cosmos DB with no expiry")
                : new PlanHistoryAvailability(false, $"the {containerName} container is partitioned on {response.Resource.PartitionKeyPath} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new PlanHistoryAvailability(false, $"the {containerName} container is not there yet; apply infra/cosmos/{containerName}.json");
        }
        catch (Exception ex)
        {
            return _availability = new PlanHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    public async Task AppendMinuteAsync(ResourceMinute minute, CancellationToken cancellation)
    {
        if ((await AvailabilityAsync(cancellation)).Available)
        {
            var document = MinuteDocument.From(minute, _minute);
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }

    public async Task AppendHourAsync(ResourceHour hour, CancellationToken cancellation)
    {
        if ((await AvailabilityAsync(cancellation)).Available)
        {
            var document = new HourDocument
            {
                Id = _hour + ":" + PlanFolding.HourKey(hour.At),
                Kind = _hour,
                Day = PlanFolding.DayOf(hour.At),
                At = hour.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                Hour = hour,
            };
            await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
        }
    }

    public async Task<IReadOnlyList<ResourceMinute>> NewestAsync(int count, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var now = DateTimeOffset.UtcNow;
        var definition = new QueryDefinition("SELECT TOP @count * FROM c WHERE c.kind = @kind AND (c.day = @today OR c.day = @yesterday) ORDER BY c.at DESC")
            .WithParameter("@count", count)
            .WithParameter("@kind", _minute)
            .WithParameter("@today", PlanFolding.DayOf(now))
            .WithParameter("@yesterday", PlanFolding.DayOf(now.AddDays(-1)));
        var documents = await ReadAll<MinuteDocument>(definition, cancellation);
        return documents.Select(document => document.ToMinute()).OrderBy(minute => minute.At).ToList();
    }

    // #region resource-buckets
    public async Task<IReadOnlyList<ResourceBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, IReadOnlyList<string> readings, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        string key = grain switch
        {
            MachineGrain.FiveMinutes => "b5",
            MachineGrain.Hour => "h1",
            _ => "h4",
        };
        // Each reading's name is checked against a plain pattern before it goes into the text,
        // since a name is code's, never a request's; the bounds are parameters.
        foreach (string name in readings)
        {
            if (!name.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_'))
            {
                throw new ArgumentException($"'{name}' is not a reading name", nameof(readings));
            }
        }
        string columns = string.Join(", ", readings.Select(name => $"AVG(c.v.{name}) AS m_{name}, MAX(c.v.{name}) AS x_{name}, SUM(c.v.{name}) AS s_{name}"));
        var definition = new QueryDefinition($"SELECT c.{key} AS bucket, COUNT(1) AS minutes, MAX(c.version) AS version{(columns.Length > 0 ? ", " + columns : "")} FROM c WHERE c.kind = @kind AND c.day >= @day AND c.at >= @at GROUP BY c.{key}")
            .WithParameter("@kind", _minute)
            .WithParameter("@day", PlanFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var rows = await ReadAll<JsonElement>(definition, cancellation);
        double? read(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? Math.Round(value.GetDouble(), 2) : null;
        return rows
            .Where(row => row.TryGetProperty("bucket", out var b) && b.ValueKind == JsonValueKind.String)
            .OrderBy(row => row.GetProperty("bucket").GetString(), StringComparer.Ordinal)
            .Select(row => new ResourceBucket(
                DateTimeOffset.Parse(row.GetProperty("bucket").GetString() + ":00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                (int)Math.Round(read(row, "minutes") ?? 0),
                readings.ToDictionary(name => name, name => read(row, "m_" + name)),
                readings.ToDictionary(name => name, name => read(row, "x_" + name)),
                readings.ToDictionary(name => name, name => read(row, "s_" + name)),
                row.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null))
            .ToList();
    }
    // #endregion resource-buckets

    public async Task<IReadOnlyList<ResourceHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }
        var definition = new QueryDefinition("SELECT * FROM c WHERE c.kind = @kind AND c.day >= @day AND c.at >= @at")
            .WithParameter("@kind", _hour)
            .WithParameter("@day", PlanFolding.DayOf(since))
            .WithParameter("@at", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var documents = await ReadAll<HourDocument>(definition, cancellation);
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

    /// <summary>One minute of one resource, its readings under <c>v</c>.</summary>
    private sealed class MinuteDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "minute";
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("b5")] public string B5 { get; set; } = "";
        [JsonPropertyName("h1")] public string H1 { get; set; } = "";
        [JsonPropertyName("h4")] public string H4 { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("v")] public Dictionary<string, double?> V { get; set; } = [];

        public static MinuteDocument From(ResourceMinute minute, string kind) => new()
        {
            Id = kind + ":" + minute.At.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            Kind = kind,
            Day = PlanFolding.DayOf(minute.At),
            At = minute.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            B5 = MachineWindows.KeyOf(minute.At, MachineGrain.FiveMinutes),
            H1 = MachineWindows.KeyOf(minute.At, MachineGrain.Hour),
            H4 = MachineWindows.KeyOf(minute.At, MachineGrain.FourHours),
            Version = minute.Version,
            // Absent rather than null, so the store's averages skip a reading nobody took.
            V = minute.Values.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value),
        };

        public ResourceMinute ToMinute() =>
            new(DateTimeOffset.Parse(At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), V, Version);
    }

    /// <summary>One hour of one resource's slower facts.</summary>
    private sealed class HourDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "hour";
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
        [JsonPropertyName("hour")] public ResourceHour Hour { get; set; } = null!;
    }
}
