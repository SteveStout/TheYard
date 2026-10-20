// What the host says about its stores the moment it starts: the loggers attached to them, each
// store's state said out loud, and an invented signing key named as such.
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// Attaches the host's loggers to the stores and says what came up. Entity Framework's own command
/// log goes where every other log line goes, which the Admin tab's log section and a test both
/// rely on, and the document store writes one line per operation to the same place from here on
/// (ADR: What the store is actually doing, addendum). Runs first of the startup services, so the
/// catalogue's warm is logged.
/// </summary>
public sealed class StoreAnnouncement(
    Backends backends,
    RelationalStore relational,
    DocumentStore document,
    StoreLoggers storeLoggers,
    SigningKeyOrigin signingKey,
    ILoggerFactory loggers,
    ILogger<StoreAnnouncement> logger) : IHostedService
{
    /// <summary>Attaches the loggers and writes the startup lines.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        storeLoggers.Factory = loggers;
        (relational.Backend.Contexts as ContextFactory)?.Attach(loggers);
        document.Store?.Logger = loggers.CreateLogger<CosmosStore>();

        // The promise the accounts record makes: an invented signing key is said out loud,
        // because its consequence is every session ending when this process does (ADR: Accounts
        // and per-user bids). Nothing about the key itself is logged.
        if (!signingKey.Configured)
        {
            logger.LogWarning(
                "No usable Auth:SigningKey is configured (none, a placeholder, or under {Bytes} bytes); "
                + "this process signs cookies with a key it invented at startup, so every session ends with it",
                TokenIssuer.MinimumKeyBytes);
        }

        foreach (var backend in backends.All)
        {
            if (backend.Database.Ready)
            {
                logger.LogInformation("Database ready: {Note}", backend.Database.Note);
            }
            else
            {
                // "The store" rather than "the database": preparing also reads the seed files, so
                // this line covers a missing dataset as well as a database that will not open.
                // The exception goes in the exception slot, not the template: the template reaches
                // the Admin tab's public log section, and a SqlException names the server, the
                // login and this container's address.
                logger.LogError(
                    backend.Database.Failure,
                    "The store could not be prepared, which covers both the database and the seed files it fills from. The catalogue is being served from the JSON files and bids will not outlive this process: {Note}",
                    backend.Database.Note);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
