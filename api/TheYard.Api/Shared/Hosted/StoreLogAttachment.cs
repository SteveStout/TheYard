// Where the host's loggers are attached to the stores: Entity Framework's command log and the
// document store's operation log both end up where every other log line goes. Its own file
// because attaching a logger and saying what came up are two jobs, and the hosted service that
// runs them in order at start is a third.

namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Attaches the host's logger factory to the stores. Entity Framework's own command log goes
/// where every other log line goes, which the Admin tab's log section and a test both rely on,
/// and the document store writes one line per operation to the same place from here on (ADR:
/// What the store is actually doing, addendum). The factory is also left on
/// <see cref="StoreLoggers"/>, so a context factory made later - the second chance - finds it.
/// </summary>
/// <param name="relational">The relational contexts, whose factory the command log is attached to.</param>
/// <param name="document">The document store, whose recorder writes one line per operation.</param>
/// <param name="storeLoggers">Where a store that comes up later reads the factory from.</param>
/// <param name="loggers">The host's logger factory, the one thing all three are given.</param>
public sealed class StoreLogAttachment(
    RelationalContexts relational,
    DocumentStore document,
    StoreLoggers storeLoggers,
    ILoggerFactory loggers)
{
    /// <summary>Attaches the factory to both stores and leaves it where a later one can find it.</summary>
    public void Attach()
    {
        storeLoggers.Factory = loggers;
        (relational.Contexts as ContextFactory)?.Attach(loggers);
        document.Store?.Recorder.Logger = loggers.CreateLogger<CosmosStore>();
    }
}
