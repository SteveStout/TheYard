// The address this container can dial itself on. The page sweep and the keep-warm loop both
// call their own container and both need the address the server actually bound; it is its
// own file because the two share it and it has nothing to do with either one's job.
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace TheYard.Api;

// #region self-address
/// <summary>
/// The address this container can dial itself on, read from the server rather
/// than from a variable somebody set earlier.
///
/// <para>Earlier is the word that matters. The proof reads the bound address
/// in a callback on <c>ApplicationStarted</c>, and a <c>CancellationToken</c>
/// runs its callbacks in the reverse of the order they were registered, so a
/// sweep registered after the proof runs before the proof's callback and would
/// find that variable still null. Registering in the other order would only
/// move the trap; asking the server, which knows what it is listening on,
/// takes the order out of it.</para>
///
/// <para>A bound address is not always one a client can dial: Kestrel reports
/// the wildcard it bound, and `localhost` inside a container resolves to a
/// stack that may not be the one it bound. Both become the loopback. HTTPS
/// addresses are skipped: this container serves plain HTTP behind the edge,
/// and a self-signed hop would be a certificate question rather than a page
/// check.</para>
/// </summary>
public static class SelfAddress
{
    /// <summary>The first address the running server is bound to that a client can dial, or null when there is none (as under the test host).</summary>
    public static string? Of(IServiceProvider services)
    {
        var addresses = services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        foreach (string address in addresses ?? (IEnumerable<string>)[])
        {
            if (Dialable(address) is { } dialable)
            {
                return dialable;
            }
        }
        return null;
    }

    /// <summary>One bound address as something a client can dial, or null when it is not one this can use.</summary>
    public static string? Dialable(string address)
    {
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string normalized = address
            .Replace("://+:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://*:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://[::]:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://0.0.0.0:", "://127.0.0.1:", StringComparison.Ordinal)
            .Replace("://localhost:", "://127.0.0.1:", StringComparison.Ordinal);

        return Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            ? parsed.GetLeftPart(UriPartial.Authority)
            : null;
    }
}
// #endregion self-address
