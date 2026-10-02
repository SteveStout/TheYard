// Where the performance proof sends its requests. A type of its own so production can hand
// the runner this container's loopback address and a test can hand it the test host's
// client, without the runner knowing which one it has.
namespace TheYard.Api;

/// <summary>
/// Where the proof's requests go: this container's own loopback address in
/// production, and whatever a test hands in. A factory rather than a client,
/// because a run wants a fresh client and a test wants to choose it.
/// </summary>
public sealed class ProofClients(Func<HttpClient> create)
{
    /// <summary>A fresh client for one run, pointed at wherever the proof's requests go.</summary>
    public HttpClient Create() => create();
}
