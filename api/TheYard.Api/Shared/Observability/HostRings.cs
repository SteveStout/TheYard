// The four rings this process keeps in memory, as one thing. Its own file because "the rings
// under the host" is a concern the record names (ADR: One host per process, the rings under
// them), and because a reader who wants to know which rings there are should find them in a
// list rather than by counting constructor parameters in the classes that read them.
namespace TheYard.Api.Shared.Observability;

/// <summary>
/// The rings under this host: the database's statements, the document store's operations, the
/// log and the requests. All four are this process's own memory, they empty on every roll, and
/// they are made in the composition root before the stores that feed them.
///
/// <para>They travel together. A reading that wants one ring usually wants two, so handing four
/// separate buffers to the classes that read them put seven and eight parameters on constructors
/// whose job is singular, which is the shape <c>wide_service_classes</c> counts. This is one
/// concern named once, not a bag of unrelated arguments: adding a fifth ring to the host adds a
/// property here and changes no constructor.</para>
/// </summary>
/// <param name="sql">The database's statements, as the SQL card reads them.</param>
/// <param name="store">The document store's operations, as the store card reads them.</param>
/// <param name="logs">This application's own log lines, as the log section reads them.</param>
/// <param name="requests">The requests served, a minute at a time, as the traffic chart reads them.</param>
public sealed class HostRings(SqlRingBuffer sql, StoreRingBuffer store, LogRingBuffer logs, RequestRingBuffer requests)
{
    /// <summary>The database's statements.</summary>
    public SqlRingBuffer Sql => sql;

    /// <summary>The document store's operations.</summary>
    public StoreRingBuffer Store => store;

    /// <summary>This application's own log lines.</summary>
    public LogRingBuffer Logs => logs;

    /// <summary>The requests served, a minute at a time.</summary>
    public RequestRingBuffer Requests => requests;
}
