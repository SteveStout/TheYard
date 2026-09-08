namespace TheYard.Application;

// #region store-log-port
// The document store's counterpart to the SQL log (ADR: What the store is
// actually doing). A parallel port rather than a widening of ISqlLog, because
// the two record different things: a SQL statement has text, parameters and a
// duration; a Cosmos DB operation has a container, a kind, a partition, a
// request charge and a duration, and only sometimes any text at all. One type
// for both would carry nulls on every row, on both sides.
//
// The no-values rule carries over exactly. A parameter here is a
// SqlParameterShape, which has no field for a value, and a partition is a
// description of the partition rather than the key value that named it: the
// key of an account's partition is the account's id, and the key of a claim
// document is an email address.

/// <summary>
/// One operation this application sent to the document store: where, what
/// kind, what it cost in request units and in milliseconds, whether it was
/// pinned to one partition or fanned out, and the HTTP request that caused it.
/// </summary>
public sealed record StoreOperation(
    DateTimeOffset At,
    string Container,
    string Kind,
    string Text,
    IReadOnlyList<SqlParameterShape> Parameters,
    string Partition,
    int PhysicalPartitions,
    double RequestCharge,
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
