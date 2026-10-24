// The document shapes of the plan container: one minute of the App Service plan, and one hour of
// its slower facts, partitioned on the UTC day and expired by the store. One file per container,
// so what a container holds is one short file to open.
using System.Globalization;
using System.Text.Json.Serialization;
using TheYard.Application.Shared.History;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

// #region plan-documents
/// <summary>
/// One minute of the plan. The three bucket keys are written with it so each window is one
/// grouped query over a key the index holds. A figure Azure did not report is absent, so an
/// average in the store skips it, the same rule the chart follows when it breaks its line.
/// </summary>
public sealed class PlanMinuteDocument
{
    /// <summary>minute:{minute}, so a minute written twice is one document.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>Always "minute", so the hour documents in the same container never join a window.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "minute";

    /// <summary>The UTC day, and the partition key.</summary>
    [JsonPropertyName("day")] public string Day { get; set; } = "";

    /// <summary>The start of the minute, round-trip UTC.</summary>
    [JsonPropertyName("at")] public string At { get; set; } = "";

    /// <summary>The five-minute, hour and four-hour bucket keys.</summary>
    [JsonPropertyName("b5")] public string B5 { get; set; } = "";

    [JsonPropertyName("h1")] public string H1 { get; set; } = "";

    [JsonPropertyName("h4")] public string H4 { get; set; } = "";

    [JsonPropertyName("cpu")] public double? Cpu { get; set; }

    [JsonPropertyName("memory")] public double? Memory { get; set; }

    [JsonPropertyName("disk_queue")] public double? DiskQueue { get; set; }

    [JsonPropertyName("http_queue")] public double? HttpQueue { get; set; }

    [JsonPropertyName("bytes_sent")] public double? BytesSent { get; set; }

    [JsonPropertyName("bytes_received")] public double? BytesReceived { get; set; }

    [JsonPropertyName("sockets")] public double? Sockets { get; set; }

    /// <summary>The build the recording site ran in this minute.</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    [JsonPropertyName("apps")] public List<PlanAppMinute> Apps { get; set; } = [];

    /// <summary>The application's minute as a document.</summary>
    public static PlanMinuteDocument From(PlanMinute minute) => new()
    {
        Id = "minute:" + Key(minute.At),
        Day = PlanFolding.DayOf(minute.At),
        At = minute.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        B5 = MachineWindows.KeyOf(minute.At, MachineGrain.FiveMinutes),
        H1 = MachineWindows.KeyOf(minute.At, MachineGrain.Hour),
        H4 = MachineWindows.KeyOf(minute.At, MachineGrain.FourHours),
        Cpu = minute.CpuPercent,
        Memory = minute.MemoryPercent,
        DiskQueue = minute.DiskQueue,
        HttpQueue = minute.HttpQueue,
        BytesSent = minute.BytesSent,
        BytesReceived = minute.BytesReceived,
        Sockets = minute.Sockets,
        Version = minute.Version,
        Apps = [.. minute.Apps],
    };

    /// <summary>The document as the application's minute.</summary>
    public PlanMinute ToMinute() => new(
        DateTimeOffset.Parse(At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Cpu, Memory, DiskQueue, HttpQueue, BytesSent, BytesReceived, Sockets, Version, Apps);

    /// <summary>A minute as sortable text, the same in every culture.</summary>
    public static string Key(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
}

/// <summary>One hour of the plan's slower facts: its size, its apps' states and the Activity Log.</summary>
public sealed class PlanHourDocument
{
    /// <summary>hour:{hour}, so an hour written twice is one document.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>Always "hour".</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "hour";

    [JsonPropertyName("day")] public string Day { get; set; } = "";

    [JsonPropertyName("at")] public string At { get; set; } = "";

    [JsonPropertyName("hour")] public PlanHour Hour { get; set; } = null!;

    /// <summary>The application's hour as a document.</summary>
    public static PlanHourDocument From(PlanHour hour) => new()
    {
        Id = "hour:" + PlanFolding.HourKey(hour.At),
        Day = PlanFolding.DayOf(hour.At),
        At = hour.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Hour = hour,
    };
}
// #endregion plan-documents
