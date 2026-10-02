// How the store gets its one client: which credential it signs in with, and the client options
// the whole application runs on. A file of its own because it is the only part that names the
// Azure identity types, and the reason it must be the only one is written on CredentialFor.
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>The connection setup of CosmosStore; the type and what it is for are described in CosmosStore.cs.</summary>
public sealed partial class CosmosStore
{
    // #region connect
    /// <summary>
    /// Connect as the container's managed identity, or as whoever is signed in
    /// to the Azure CLI on a developer's machine, and the configuration says
    /// which. There is no third way in, because the account has no keys: local
    /// authentication was disabled when it was created, so there is nothing to
    /// put in a connection string and nothing to leak (ADR: A second store on
    /// Cosmos DB, and what it costs).
    ///
    /// <para>Chosen by a setting rather than probed. Chaining the two, so that
    /// a failed identity falls through to the CLI, does not work on a machine
    /// with no identity endpoint: the probe of the instance metadata service
    /// retries for seconds and then reports an authentication failure rather
    /// than an absence, which no chain falls through. A deployed container says
    /// "managed-identity" in its environment and a developer's machine says
    /// nothing, and each gets one credential that either works or says
    /// why.</para>
    /// </summary>
    public static CosmosStore Connect(string accountEndpoint, string databaseName, string containerPrefix, string credentialKind, string managedIdentityClientId, IStoreLog log)
    {
        TokenCredential credential = CredentialFor(credentialKind, managedIdentityClientId);
        var options = new CosmosClientOptions
        {
            ApplicationName = "TheYard",
            // Session consistency is the account default and what this
            // application needs; the option is set here so the choice is in
            // the code and not only in the portal (ADR: A second store on
            // Cosmos DB, and what it costs).
            ConsistencyLevel = ConsistencyLevel.Session,
            UseSystemTextJsonSerializerWithOptions = Json,
            // The SDK retries a 429 on its own. Nine tries over thirty seconds
            // is what a seed against a 1000 RU/s database needs; a request that
            // is still being throttled after that is a request worth failing.
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
        };
        var client = new CosmosClient(accountEndpoint, credential, options);
        return new CosmosStore(client, databaseName, containerPrefix, log);
    }

    /// <summary>
    /// The one rule above, on its own so the other Azure client in this
    /// application (the email sender, ADR: Accounts and per-user bids,
    /// addendum) authenticates the same way as the store, as the same identity.
    /// The credential types are named here and nowhere else on purpose: this
    /// project's graph holds the one Azure.Identity the container was proven on
    /// (the pin in the project file, and the test that holds it), while a
    /// newer Azure.Core elsewhere in the application carries a second copy of
    /// the same types and naming one there is ambiguous.
    /// </summary>
    public static TokenCredential CredentialFor(string credentialKind, string managedIdentityClientId) =>
        string.Equals(credentialKind, ManagedIdentity, StringComparison.OrdinalIgnoreCase)
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId))
            : new AzureCliCredential();

    /// <summary>The value of <c>Cosmos:Credential</c> that means the container's own identity. Anything else means the Azure CLI.</summary>
    public const string ManagedIdentity = "managed-identity";
    // #endregion connect
}
