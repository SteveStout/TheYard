// Every call this application makes to another service over HTTP (Azure Resource Manager, Azure Monitor,
// Log Analytics, the cost API, the registry, Netlify, GitHub, Anthropic, its own loopback) opens its socket here,
// in Infrastructure, and nowhere in the host: the host builds a request as data and hands it to an Outbound
// (ADR: The onion cut into vertical slices, rule 8). One connection pool serves the readers, because the pool and
// the suite's seam into it are one decision (ADR: One frame for every resource).
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TheYard.Infrastructure.Shared.Http;

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
    /// <param name="timeout">How long one call may take.</param>
    public static Outbound Client(TimeSpan timeout) => new(new HttpClient(Routed.Value ?? Handler, disposeHandler: false) { Timeout = timeout });

    /// <summary>
    /// A client to this container's own address, with cookies off: the keep-warm loop, the performance proof and the
    /// page sweep each read the site the way a visitor does, and the proof holds one session per store by hand, which
    /// a cookie jar would merge. Its own connections, because the loopback is not a host the pool's readers call.
    /// </summary>
    /// <param name="address">The address the server listens on.</param>
    /// <param name="timeout">How long one call may take.</param>
    public static Outbound Loopback(Uri address, TimeSpan timeout) =>
        new(new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = address, Timeout = timeout });

    /// <summary>Sends this flow's outbound calls to <paramref name="handler"/> until the returned scope is disposed. The suite's seam.</summary>
    /// <param name="handler">What answers in place of the network.</param>
    public static IDisposable RouteTo(HttpMessageHandler handler)
    {
        var before = Routed.Value;
        Routed.Value = handler;
        return new Restore(before);
    }

    /// <summary>
    /// The last day of the leaf certificate <paramref name="site"/> answers with, from one handshake with a handler
    /// of its own (the pool's connections are already open and would show no certificate); null when it cannot be
    /// read. The platform's own verdict on the chain is kept: an invalid chain still fails.
    /// </summary>
    /// <param name="site">The site's public https address.</param>
    /// <param name="cancellation">The reading's cancellation.</param>
    public static async Task<DateTimeOffset?> CertificateNotAfterAsync(Uri site, CancellationToken cancellation)
    {
        DateTimeOffset? notAfter = null;
        using var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is X509Certificate2 leaf)
                    {
                        notAfter = new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                    }
                    return errors == SslPolicyErrors.None;
                },
            },
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var response = await client.GetAsync(new Uri(site, "/healthz"), cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // No answer is no reading; the card says it was not read.
        }
        return notAfter;
    }

    private sealed class Restore(HttpMessageHandler? before) : IDisposable
    {
        public void Dispose() => Routed.Value = before;
    }
}

/// <summary>
/// One caller's way out over HTTP. The host holds this, never an HttpClient: it builds the request and reads the
/// answer, and the socket is opened here. The members are the ones the readers use.
/// </summary>
/// <param name="client">The client this wraps; disposing the Outbound disposes it.</param>
public sealed class Outbound(HttpClient client) : IDisposable
{
    /// <summary>An Outbound over a client somebody else built: a test's in-memory server, or a recorded handler.</summary>
    /// <param name="client">The client.</param>
    public static Outbound Over(HttpClient client) => new(client);

    /// <summary>The address relative paths are read against, or null.</summary>
    public Uri? BaseAddress => client.BaseAddress;

    /// <summary>Sends a request the caller built.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation = default) => client.SendAsync(request, cancellation);

    /// <summary>Sends a request the caller built, answering once the headers arrive when <paramref name="completion"/> says so.</summary>
    /// <param name="request">The request.</param>
    /// <param name="completion">When the call counts as answered.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellation) => client.SendAsync(request, completion, cancellation);

    /// <summary>A GET.</summary>
    /// <param name="address">The address, absolute or relative to <see cref="BaseAddress"/>.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> GetAsync(Uri address, CancellationToken cancellation = default) => client.GetAsync(address, cancellation);

    /// <summary>A GET that counts as answered when <paramref name="completion"/> says, the headers alone or the whole body.</summary>
    /// <param name="address">The address, absolute or relative to <see cref="BaseAddress"/>.</param>
    /// <param name="completion">When the call counts as answered.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> GetAsync(Uri address, HttpCompletionOption completion, CancellationToken cancellation) => client.GetAsync(address, completion, cancellation);

    /// <summary>A GET.</summary>
    /// <param name="address">The address, absolute or relative to <see cref="BaseAddress"/>.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> GetAsync(string address, CancellationToken cancellation = default) => client.GetAsync(address, cancellation);

    /// <summary>A POST of <paramref name="content"/>.</summary>
    /// <param name="address">The address.</param>
    /// <param name="content">The body.</param>
    /// <param name="cancellation">The call's cancellation.</param>
    public Task<HttpResponseMessage> PostAsync(string address, HttpContent content, CancellationToken cancellation = default) => client.PostAsync(address, content, cancellation);

    /// <summary>Disposes the client; a client over the shared pool leaves the pool alone.</summary>
    public void Dispose() => client.Dispose();
}
// #endregion outbound-http
