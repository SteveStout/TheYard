using System.Text.Json.Serialization;

namespace TheYard.Application.Shared.Stores;

// #region store-log-port
// The document store's counterpart to the SQL log (ADR: What the store is
// actually doing). A parallel port rather than a widening of IStatementLog, because
// the two record different things: a statement has text, parameters and a
// duration; a document store operation has a container, a kind, a scope, a
// charge and a duration, and only sometimes any text at all. One type
// for both would carry nulls on every row, on both sides.
//
// The no-values rule carries over exactly. A parameter here is a
// ParameterShape, which has no field for a value, and a scope is a
// description of the partition rather than the key value that named it: the
// key of an account's partition is the account's id, and the key of a claim
// document is an email address.

/// <summary>
/// One operation this application sent to the document store: where, what
/// kind, what it charged and how many milliseconds it took, whether it was
/// pinned to one partition or fanned out, and the HTTP request that caused it.
/// </summary>
/// <param name="At">When the operation ran.</param>
/// <param name="Container">The container the operation went to.</param>
/// <param name="Kind">The kind of operation, one of <see cref="StoreOperationKind"/>.</param>
/// <param name="Text">The query text, or empty when the operation has none.</param>
/// <param name="Parameters">The shape of each query parameter: name, type and size, never the value.</param>
/// <param name="Scope">A description of the partition the operation was pinned to, or that it fanned out; never the key value. On the wire as <c>partition</c>.</param>
/// <param name="Shards">How many physical partitions the container has for the operation to reach. On the wire as <c>physical_partitions</c>.</param>
/// <param name="Charge">What the operation cost, in the store's own unit (request units). On the wire as <c>request_charge</c>.</param>
/// <param name="DurationMs">How long the operation took, in milliseconds.</param>
/// <param name="Outcome">The status code, or the failure the operation met.</param>
/// <param name="Request">The HTTP request that caused the operation, like "GET /api/vehicles", or null when none did.</param>
/// <param name="RequestId">The trace identifier of that HTTP request, or null when there was none.</param>
public sealed record StoreOperation(
    DateTimeOffset At,
    string Container,
    string Kind,
    string Text,
    IReadOnlyList<ParameterShape> Parameters,
    [property: JsonPropertyName("partition")] string Scope,
    [property: JsonPropertyName("physical_partitions")] int Shards,
    [property: JsonPropertyName("request_charge")] double Charge,
    long DurationMs,
    string Outcome,
    string? Request,
    string? RequestId);

/// <summary>The kinds of operation the store log distinguishes. Strings, because they are read by a page.</summary>
public static class StoreOperationKind
{
    public const string PointRead = "point read";
    public const string PointWrite = "point write";
    public const string PointDelete = "point delete";
    public const string Query = "query";
    public const string Batch = "batch";
    public const string Metadata = "metadata";
}

/// <summary>Port: where recent document store operations are kept for the Admin tab.</summary>
public interface IStoreLog
{
    void Record(StoreOperation operation);
}

/// <summary>The port wired to nothing, for the tests and the relational containers.</summary>
public sealed class NullStoreLog : IStoreLog
{
    public static readonly NullStoreLog Instance = new();

    private NullStoreLog() { }

    public void Record(StoreOperation operation) { }
}
// #endregion store-log-port
