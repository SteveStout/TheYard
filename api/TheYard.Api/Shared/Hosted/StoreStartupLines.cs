// What the host says about its stores the moment it starts: each store's state said out loud and
// an invented signing key named as such. Its own file because saying what came up is one job, and
// what the sentences actually are is the part a reader comes looking for.

namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Writes the startup lines about the stores and the signing key. One line per store saying
/// whether it is ready, and a warning when the key was invented rather than configured, because
/// that one has a consequence a reader of the log needs to know about.
/// </summary>
/// <param name="backends">Every store this process brought up, each with its own state.</param>
/// <param name="signingKey">Whether the cookie signing key was configured or invented at startup.</param>
/// <param name="logger">Where the lines go.</param>
public sealed class StoreStartupLines(Backends backends, SigningKeyOrigin signingKey, ILogger<StoreStartupLines> logger)
{
    /// <summary>Writes the signing key's warning, where one is owed, and then a line per store.</summary>
    public void Write()
    {
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
    }
}
