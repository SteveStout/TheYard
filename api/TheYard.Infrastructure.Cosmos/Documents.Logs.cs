// The document shape of the logs container: one kept event per document, partitioned on the UTC
// day, written once and expired by the store. One file per container, so what a container holds
// is one short file to open; the conversion to and from the application's log event sits on
// the document itself.
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
// #region log-documents
/// <summary>
/// One kept log event as a document in the store, with its day and fields as
/// strings, partitioned on the day it happened. Written once and never
/// updated, which is what makes a transactional batch of a hundred the right
/// write and the container's time-to-live the right retention (ADR: Logs that
/// outlive the container). Every string arrived through LogText.Clean, so no
/// field can carry an at sign.
/// </summary>
public sealed class LogDocument
{
    /// <summary>{at as ticks}:{random}, so two events in the same tick on two containers are two documents.</summary>
    public string Id { get; set; } = "";

    /// <summary>The UTC day the event happened, and the partition key (<c>day</c>).</summary>
    public string Day { get; set; } = "";

    /// <summary>When the event happened, as a round-trip UTC timestamp (<c>at</c>).</summary>
    public string At { get; set; } = "";

    /// <summary>Which kind of event this is: request, error, app or a kept ring kind (<c>kind</c>).</summary>
    public string Kind { get; set; } = "";

    /// <summary>The site that wrote the event (<c>store</c>).</summary>
    public string Store { get; set; } = "";

    /// <summary>The log level, such as Warning or Error (<c>level</c>).</summary>
    public string Level { get; set; } = "";

    /// <summary>The logger category or exception type that produced the event (<c>category</c>).</summary>
    public string Category { get; set; } = "";

    /// <summary>The HTTP method of the request (<c>method</c>).</summary>
    public string Method { get; set; } = "";

    /// <summary>The request path, cleaned (<c>path</c>).</summary>
    public string Path { get; set; } = "";

    /// <summary>The HTTP status code the request answered with (<c>status</c>).</summary>
    public int Status { get; set; }

    /// <summary>How long the request took, in milliseconds (<c>duration_ms</c>).</summary>
    public long DurationMs { get; set; }

    /// <summary>The day's keyed hash of the visitor, never the visitor (<c>visitor</c>).</summary>
    public string Visitor { get; set; } = "";

    /// <summary>The caller's address cut to three octets (<c>network</c>).</summary>
    public string Network { get; set; } = "";

    /// <summary>The error or warning message, cleaned and bounded (<c>message</c>).</summary>
    public string Message { get; set; } = "";

    /// <summary>Extra detail such as a bounded stack, cleaned and bounded (<c>detail</c>).</summary>
    public string Detail { get; set; } = "";

    /// <summary>The trace id that ties the event to its request (<c>trace_id</c>).</summary>
    public string TraceId { get; set; } = "";

    /// <summary>A kept ring entry, as the ring serves it (ADR: Logs that outlive the container, the addendum on the cards); absent on the three kinds the keyed log reads, which is how its query tells them apart.</summary>
    public JsonElement? Entry { get; set; }

    /// <summary>The store's own field: seconds this document lives, where that is shorter than the container's three years. Absent, the container decides.</summary>
    public int? Ttl { get; set; }

    /// <summary>The document for one event, filed under the day it happened.</summary>
    public static LogDocument From(LogEvent e, string day) => new()
    {
        Id = $"{e.At.UtcTicks}:{Guid.NewGuid():N}",
        Day = day,
        At = e.At.ToUniversalTime().ToString("O"),
        Kind = e.Kind,
        Store = e.Store,
        Level = e.Level,
        Category = e.Category,
        Method = e.Method,
        Path = e.Path,
        Status = e.Status,
        DurationMs = e.DurationMs,
        Visitor = e.Visitor,
        Network = e.Network,
        Message = e.Message,
        Detail = e.Detail,
        TraceId = e.TraceId,
        Entry = Parsed(e.Entry),
        Ttl = e.TtlSeconds,
    };

    /// <summary>The ring's JSON as a value the serializer writes as it stands; the document it was parsed in is let go.</summary>
    private static JsonElement? Parsed(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>The event this document holds, read back. The ring entry and the lifetime stay behind: the keyed log's reader does not use them.</summary>
    public LogEvent ToEvent() => new(
        DateTimeOffset.Parse(At, null, System.Globalization.DateTimeStyles.RoundtripKind),
        Kind, Store, Level, Category, Method, Path, Status, DurationMs, Visitor, Network, Message, Detail, TraceId);
}
// #endregion log-documents
// #endregion documents
