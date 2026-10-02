// The two numbers the store log needs from any response: its charge and its status. Its own
// file because the store's wrapper reads every response through it, whatever the SDK's type.
namespace TheYard.Infrastructure.Cosmos;

/// <summary>What one response cost, in request units and as a status code, read off whichever response type the SDK answered with.</summary>
/// <param name="Charge">What the response cost, in request units.</param>
/// <param name="Status">The HTTP status code of the response.</param>
public readonly record struct Cost(double Charge, int Status);
