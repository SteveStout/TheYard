// The wrapper every call to the document store goes through: it times the call, reads its charge,
// and writes one line to the store log and one to the console. A class of its own because the store
// with it was over three hundred lines (ADR-088), and because what is written, and never written,
// is one subject (ADR: What the store is actually doing).
using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

/// <summary>Times every operation of a <see cref="CosmosStore"/> and writes what it cost.</summary>
/// <param name="log">The store log every operation's charge and duration is written to.</param>
/// <param name="prefix">What goes in front of every catalog name to make the real container name.</param>
public sealed class CosmosOperationLog(IStoreLog log, string prefix)
{
    /// <summary>
    /// What request is in flight, asked at record time. Set by the host after
    /// the container is built, because the request describer lives in DI and
    /// the store is created before DI exists. Null outside a request, which is
    /// what a startup operation records.
    /// </summary>
    public ICurrentRequest CurrentRequest { get; set; } = NoCurrentRequest.Instance;

    /// <summary>
    /// The application's logger, set by the host once there is an application,
    /// the way the relational side attaches Entity Framework's command logging
    /// after the container is built. One line per operation at Information,
    /// so the console, and the Admin tab's log card that mirrors it, show the
    /// document store's traffic the way they show every SQL statement
    /// (ADR: What the store is actually doing, addendum). The line carries the
    /// kind, the container, the charge, the time, the partition described and
    /// the query shape; never a value, for the reason the store log gives.
    /// Silent until attached. The store hands this out as its Recorder.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>A container's catalog name: its real name with the prefix taken off, which is the name the store log shows.</summary>
    internal string NameOf(Container container) =>
        container.Id.StartsWith(prefix, StringComparison.Ordinal) ? container.Id[prefix.Length..] : container.Id;

    // #region timed
    /// <summary>
    /// Run one operation and write its charge and its duration to the store
    /// log, whether it succeeded or not. The store log is a public page: this
    /// records the kind, the container, the shape of the parameters and the
    /// charge, and never an id that could be an address or a value that could
    /// be anything (ADR: What the store is actually doing).
    /// </summary>
    internal async Task<TResponse> Timed<TResponse>(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, Func<Task<TResponse>> operation, Func<TResponse, Cost> cost)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await operation();
            var paid = cost(response);
            Record(container, kind, text, parameters, partitionLabel, physical, paid.Charge, clock.Elapsed, paid.Status.ToString());
            return response;
        }
        catch (CosmosException ex)
        {
            Record(container, kind, text, parameters, partitionLabel, physical, ex.RequestCharge, clock.Elapsed, "failed: " + (int)ex.StatusCode);
            throw;
        }
        catch (Exception ex)
        {
            // A refused batch throws its own exception rather than the SDK's,
            // and it is recorded here too, so the one operation that failed is
            // never the one operation the page cannot show.
            Record(container, kind, text, parameters, partitionLabel, physical, 0, clock.Elapsed, "failed: " + ex.GetType().Name);
            throw;
        }
    }

    // #endregion timed

    // #region record
    /// <summary>
    /// Write one finished operation to the store log and as one console line:
    /// the kind, the container, the charge, the time, the partition described,
    /// the outcome and the query text, and never a parameter's value.
    /// </summary>
    internal void Record(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, double charge, TimeSpan elapsed, string outcome)
    {
        // Nothing in here may break the operation it observed
        // (ADR: What the database is actually doing).
        try
        {
            string name = NameOf(container);
            double rounded = Math.Round(charge, 2);
            long ms = (long)elapsed.TotalMilliseconds;
            log.Record(new StoreOperation(
                DateTimeOffset.UtcNow,
                name,
                kind,
                text,
                parameters,
                partitionLabel,
                physical,
                rounded,
                ms,
                outcome,
                CurrentRequest.Describe(),
                CurrentRequest.Identify()));
            // The same operation as one console line, the shape Entity
            // Framework gives a statement: what ran, what it cost, how long,
            // and the query with its parameters by name and never by value.
            Logger.LogInformation(
                "Executed Cosmos DB {Kind} on {Container} ({Charge} RU, {Ms} ms, {Partition}) {Outcome}: {Text}",
                kind, name, rounded, ms, partitionLabel, outcome, text);
        }
        catch
        {
            // Deliberately silent: a failure to record must never become a
            // failure of the operation, the same reason the SQL interceptor gives.
        }
    }
    // #endregion record
}
