// Every call this host makes to another service over HTTP (Azure Resource Manager, Azure Monitor,
// Log Analytics, the cost API, the registry, Netlify) goes through one connection pool. Its own
// file because the pool and the suite's seam into it are one decision (ADR: One frame for every resource).

namespace TheYard.Api.Shared.Instance;

// #region outbound-http
/// <summary>
/// One <see cref="SocketsHttpHandler"/> for the process, and a cheap client over it per call. This
/// is the pooling IHttpClientFactory gives code that is built by dependency injection, in the
/// form Microsoft documents for code that is not: the handler holds the connections and is never
/// disposed, each connection is retired after two minutes so a changed DNS answer is picked up,
/// and a client is a wrapper that carries one caller's timeout. Seven readers each held a static
/// client of their own before this, seven pools for the same few hosts.
/// </summary>
public static class OutboundHttp
{
    private static readonly SocketsHttpHandler Handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    };

    /// <summary>
    /// A handler that answers in place of the network for the flow that set it, and only that
    /// flow: a test routes its own reader's calls to recorded answers while every other test,
    /// running beside it, still sees the real pool (and, having no credential, reads nothing).
    /// </summary>
    private static readonly AsyncLocal<HttpMessageHandler?> Routed = new();

    /// <summary>A client over the shared pool with one caller's timeout; disposing it leaves the pool alone.</summary>
    public static HttpClient Client(TimeSpan timeout) => new(Routed.Value ?? Handler, disposeHandler: false) { Timeout = timeout };

    /// <summary>Sends this flow's outbound calls to <paramref name="handler"/> until the returned scope is disposed. The suite's seam.</summary>
    public static IDisposable RouteTo(HttpMessageHandler handler)
    {
        var before = Routed.Value;
        Routed.Value = handler;
        return new Restore(before);
    }

    private sealed class Restore(HttpMessageHandler? before) : IDisposable
    {
        public void Dispose() => Routed.Value = before;
    }
}
// #endregion outbound-http
