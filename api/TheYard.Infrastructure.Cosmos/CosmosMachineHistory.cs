using System.Net;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// What the machines were doing, kept in the document store (ADR: What the
/// machines are doing). One container partitioned on the UTC day, one document
/// a minute from each site, and the container's own time-to-live as the
/// retention: a minute expires a month after it is written and nothing has to
/// run to delete it.
///
/// <para>Why this store. A minute is written once and read rarely, it costs a
/// few request units against an allowance of a thousand a second that this
/// site never approaches, and both sites write to the one account, so either
/// site's Admin tab could read the other's month. The relational side would
/// take the same rows, and would be a table nothing else joins to.</para>
///
/// <para>Like the log and activity adapters, this one calls the container
/// directly rather than through the measured helpers in
/// <see cref="CosmosStore"/>: a write a minute must not appear in the store
/// log whose request units it is partly recording. What it costs is counted
/// here and reported with the card.</para>
/// </summary>
public sealed class CosmosMachineHistory(CosmosStore store) : IMachineHistory
{
    private readonly Container _container = store.ContainerNamed(Containers.Machines);
    private MachineHistoryAvailability? _availability;
    private double _charge;
    private int _operations;
    private int _failures;

    /// <summary>What keeping the minutes has cost the store since the process started: request units, operations, failures.</summary>
    public (double Charge, int Operations, int Failures) Cost => (Math.Round(_charge, 2), _operations, _failures);

    public async Task<MachineHistoryAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }

        try
        {
            var response = await _container.ReadContainerAsync(cancellationToken: cancellation);
            Count(response.RequestCharge);
            string key = response.Resource.PartitionKeyPath;
            int? ttl = response.Resource.DefaultTimeToLive;
            return _availability = key == Containers.PartitionKeyPaths[Containers.Machines]
                ? new MachineHistoryAvailability(true, ttl is > 0 ? $"kept in Azure Cosmos DB for {ttl.Value / 86_400} days" : "kept in Azure Cosmos DB with no expiry")
                : new MachineHistoryAvailability(false, $"the machines container is partitioned on {key} and the code was written for /day");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return _availability = new MachineHistoryAvailability(false, "the machines container is not there; apply infra/cosmos/machines.json");
        }
        catch (Exception ex)
        {
            return _availability = new MachineHistoryAvailability(false, $"the store could not be asked: {ex.GetType().Name}");
        }
    }

    // #region append-minute
    public async Task AppendAsync(MachineMinute minute, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return;
        }

        var document = MachineMinuteDocument.From(minute);
        try
        {
            // An upsert, so a minute written twice (a roll inside the minute,
            // two processes briefly alive at once) is one document, the later.
            var response = await _container.UpsertItemAsync(document, new PartitionKey(document.Day), cancellationToken: cancellation);
            Count(response.RequestCharge);
        }
        catch (CosmosException ex)
        {
            Count(ex.RequestCharge);
            _failures++;
        }
    }
    // #endregion append-minute

    // #region grouped-query
    public async Task<IReadOnlyList<MachineBucket>> QueryAsync(string site, DateTimeOffset since, MachineGrain grain, CancellationToken cancellation)
    {
        if (!(await AvailabilityAsync(cancellation)).Available)
        {
            return [];
        }

        // The bucket key is one of three fields written with every minute, so
        // the window is a GROUP BY over a key the index holds. The field name
        // comes from an enum and never from a request; the site and the two
        // bounds are parameters. The day narrows the partitions the query fans
        // out to, and the minute narrows within the first day.
        string key = grain switch
        {
            MachineGrain.FiveMinutes => "b5",
            MachineGrain.Hour => "h1",
            _ => "h4",
        };
        var definition = new QueryDefinition(
                $"SELECT c.{key} AS bucket, COUNT(1) AS minutes, MAX(c.limit_mb) AS limit_mb, AVG(c.ws_mb) AS ws_mb, MAX(c.ws_max_mb) AS ws_max_mb, "
                + "AVG(c.managed_mb) AS managed_mb, AVG(c.cpu) AS cpu, MAX(c.cpu_max) AS cpu_max, AVG(c.sql_cpu) AS sql_cpu, AVG(c.sql_memory) AS sql_memory, "
                + "AVG(c.sql_data_io) AS sql_data_io, SUM(c.ru) AS ru, SUM(c.operations) AS operations, SUM(c.requests) AS requests, "
                + "AVG(c.p50_ms) AS p50_ms, MAX(c.p95_ms) AS p95_ms, SUM(c.errors_5xx) AS errors_5xx, SUM(c.errors_4xx) AS errors_4xx "
                + $"FROM c WHERE c.day >= @day AND c.site = @site AND c.at >= @at GROUP BY c.{key}")
            .WithParameter("@day", MachineWindows.DayOf(since))
            .WithParameter("@site", site)
            .WithParameter("@at", since.ToUniversalTime().ToString("O"));

        var rows = new List<BucketRow>();
        using var feed = _container.GetItemQueryIterator<BucketRow>(definition);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellation);
            Count(page.RequestCharge);
            rows.AddRange(page);
        }

        // A grouped query comes back in no promised order, so it is sorted
        // here; the key is sortable text by construction.
        return rows
            .Where(row => !string.IsNullOrEmpty(row.Bucket))
            .OrderBy(row => row.Bucket, StringComparer.Ordinal)
            .Select(row => new MachineBucket(
                DateTimeOffset.Parse(row.Bucket + ":00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                (int)Math.Round(row.Minutes),
                Math.Round(row.LimitMb, 1),
                Math.Round(row.WsMb, 1),
                Math.Round(row.WsMaxMb, 1),
                Math.Round(row.ManagedMb, 1),
                Rounded(row.Cpu),
                Rounded(row.CpuMax),
                Rounded(row.SqlCpu),
                Rounded(row.SqlMemory),
                Rounded(row.SqlDataIo),
                Math.Round(row.Ru, 2),
                (int)Math.Round(row.Operations),
                (int)Math.Round(row.Requests),
                Rounded(row.P50Ms),
                Rounded(row.P95Ms),
                (int)Math.Round(row.Errors5xx),
                (int)Math.Round(row.Errors4xx)))
            .ToList();
    }

    private static double? Rounded(double? value) => value is null ? null : Math.Round(value.Value, 2);

    /// <summary>One group as the store answers it. An average over minutes that all lack a figure is absent from the answer, which arrives here as null.</summary>
    private sealed class BucketRow
    {
        public string Bucket { get; set; } = "";
        // Counts and sums come back as JSON numbers with no promise of being
        // written without a decimal point, so they are read as doubles.
        public double Minutes { get; set; }
        public double LimitMb { get; set; }
        public double WsMb { get; set; }
        public double WsMaxMb { get; set; }
        public double ManagedMb { get; set; }
        public double? Cpu { get; set; }
        public double? CpuMax { get; set; }
        public double? SqlCpu { get; set; }
        public double? SqlMemory { get; set; }
        public double? SqlDataIo { get; set; }
        public double Ru { get; set; }
        public double Operations { get; set; }
        public double Requests { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("p50_ms")]
        public double? P50Ms { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("p95_ms")]
        public double? P95Ms { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("errors_5xx")]
        public double Errors5xx { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("errors_4xx")]
        public double Errors4xx { get; set; }
    }
    // #endregion grouped-query

    private void Count(double charge)
    {
        _charge += charge;
        _operations++;
    }
}
