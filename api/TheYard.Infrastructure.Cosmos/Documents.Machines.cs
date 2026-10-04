// The document shape of the machines container: one minute of one site per document,
// partitioned on the UTC day, written once and expired by the store. One file per container, so
// what a container holds is one short file to open; the conversion from the application's
// minute sits on the document itself.
using System.Text.Json.Serialization;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
// #region machine-documents
/// <summary>
/// One minute of one site (ADR: What the machines are doing). Written once and
/// never updated, like a log line, and expired by the container. The three
/// bucket keys are written with the minute so that a day, a week and a month
/// are each one grouped query over a key the index already holds, rather than
/// forty thousand documents read back to be averaged here. A figure that was
/// not read is absent from the document, not null in it: the serializer leaves
/// nulls out, and an average in the store skips what is absent, which is the
/// same rule the chart follows when it breaks its line over a gap.
/// </summary>
public sealed class MachineMinuteDocument
{
    /// <summary>{site}:{minute}, so the two sites' minutes are two documents and a minute written twice is one.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The UTC day of the minute, and the partition key.</summary>
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    /// <summary>The site the minute was read on.</summary>
    [JsonPropertyName("site")]
    public string Site { get; set; } = "";

    /// <summary>The start of the minute, as a round-trip UTC timestamp.</summary>
    [JsonPropertyName("at")]
    public string At { get; set; } = "";

    /// <summary>The key of the five-minute bucket the minute falls in, which the day view groups on.</summary>
    [JsonPropertyName("b5")]
    public string B5 { get; set; } = "";

    /// <summary>The key of the hour bucket the minute falls in, which the week view groups on.</summary>
    [JsonPropertyName("h1")]
    public string H1 { get; set; } = "";

    /// <summary>The key of the four-hour bucket the minute falls in, which the month view groups on.</summary>
    [JsonPropertyName("h4")]
    public string H4 { get; set; } = "";

    /// <summary>The memory limit the container runs under, in megabytes.</summary>
    [JsonPropertyName("limit_mb")]
    public double LimitMb { get; set; }

    /// <summary>The average working set, in megabytes.</summary>
    [JsonPropertyName("ws_mb")]
    public double WsMb { get; set; }

    /// <summary>The highest working set sampled, in megabytes.</summary>
    [JsonPropertyName("ws_max_mb")]
    public double WsMaxMb { get; set; }

    /// <summary>The average managed heap size, in megabytes.</summary>
    [JsonPropertyName("managed_mb")]
    public double ManagedMb { get; set; }

    /// <summary>The average process CPU, in percent; absent when not read.</summary>
    [JsonPropertyName("cpu")]
    public double? Cpu { get; set; }

    /// <summary>The highest process CPU sampled, in percent; absent when not read.</summary>
    [JsonPropertyName("cpu_max")]
    public double? CpuMax { get; set; }

    /// <summary>The relational store's own CPU reading, in percent; absent when not read.</summary>
    [JsonPropertyName("sql_cpu")]
    public double? SqlCpu { get; set; }

    /// <summary>The relational store's own memory reading, in percent; absent when not read.</summary>
    [JsonPropertyName("sql_memory")]
    public double? SqlMemory { get; set; }

    /// <summary>The relational store's own data IO reading, in percent; absent when not read.</summary>
    [JsonPropertyName("sql_data_io")]
    public double? SqlDataIo { get; set; }

    /// <summary>What the document store charged in the minute, in request units.</summary>
    [JsonPropertyName("ru")]
    public double Ru { get; set; }

    /// <summary>How many operations went to the document store in the minute.</summary>
    [JsonPropertyName("operations")]
    public int Operations { get; set; }

    /// <summary>How many HTTP requests the site answered in the minute.</summary>
    [JsonPropertyName("requests")]
    public int Requests { get; set; }

    /// <summary>The median request duration, in milliseconds; absent when not read.</summary>
    [JsonPropertyName("p50_ms")]
    public double? P50Ms { get; set; }

    /// <summary>The 95th percentile request duration, in milliseconds; absent when not read.</summary>
    [JsonPropertyName("p95_ms")]
    public double? P95Ms { get; set; }

    /// <summary>How many requests answered with a 5xx status.</summary>
    [JsonPropertyName("errors_5xx")]
    public int Errors5xx { get; set; }

    /// <summary>How many requests answered with a 4xx status.</summary>
    [JsonPropertyName("errors_4xx")]
    public int Errors4xx { get; set; }

    /// <summary>The id of one site's minute, the UTC minute written out to the minute in the invariant culture.</summary>
    public static string IdFor(string site, DateTimeOffset minute) =>
        $"{site}:{minute.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>The document for one minute, with its day and its three bucket keys worked out from the minute.</summary>
    public static MachineMinuteDocument From(MachineMinute minute) => new()
    {
        Id = IdFor(minute.Site, minute.At),
        Day = minute.At.UtcDay,
        Site = minute.Site,
        At = minute.At.ToUniversalTime().ToString("O"),
        B5 = MachineWindows.KeyOf(minute.At, MachineGrain.FiveMinutes),
        H1 = MachineWindows.KeyOf(minute.At, MachineGrain.Hour),
        H4 = MachineWindows.KeyOf(minute.At, MachineGrain.FourHours),
        LimitMb = minute.MemoryLimitMb,
        WsMb = minute.WorkingSetMb,
        WsMaxMb = minute.WorkingSetMaxMb,
        ManagedMb = minute.ManagedMb,
        Cpu = minute.CpuPercent,
        CpuMax = minute.CpuMaxPercent,
        SqlCpu = minute.SqlCpuPercent,
        SqlMemory = minute.SqlMemoryPercent,
        SqlDataIo = minute.SqlDataIoPercent,
        Ru = minute.RequestUnits,
        Operations = minute.Operations,
        Requests = minute.Requests,
        P50Ms = minute.P50Ms,
        P95Ms = minute.P95Ms,
        Errors5xx = minute.ServerErrors,
        Errors4xx = minute.ClientErrors,
    };
}
// #endregion machine-documents
// #endregion documents
