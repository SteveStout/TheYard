// The loop that keeps every public read warm, started once the server listens and stopped with
// the host.
using Microsoft.AspNetCore.Hosting.Server;

namespace TheYard.Api.Shared.Hosted;

/// <summary>
/// Every public read, on every store, every four minutes for as long as the container runs (ADR:
/// Kept awake). It dials the address the server bound, through the whole pipeline. Registered only
/// where it is on: on App Service by default, off everywhere else, the test host included.
/// </summary>
public sealed class KeepWarmAtRoll(
    Backends backends,
    KeepWarmState state,
    IServer server,
    IHostApplicationLifetime lifetime,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    // #region keep-warm-wiring
    // One loopback client for the life of the process, cookies off, made the first time the
    // server's address is known.
    private Outbound? _client;

    /// <summary>Builds the loop and starts it once the server is listening.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var log = loggers.CreateLogger<KeepWarm>();
        var loop = new KeepWarm(
            async cancellation =>
            {
                _client ??= SelfAddress.Of(server) is { } dialable
                    ? OutboundHttp.Loopback(new Uri(dialable), TimeSpan.FromSeconds(30))
                    : null;
                return _client is null
                    ? new KeepWarmPass(DateTimeOffset.UtcNow, 0, 0, 0, null)
                    : await KeepWarmReads.RunAsync(_client, backends.All.Select(backend => backend.Key).ToList(), TimeProvider.System, log, cancellation);
            },
            TimeProvider.System,
            KeepWarm.RandomStagger(),
            log);
        state.Loop = loop;
        lifetime.ApplicationStarted.Register(() => _ = loop.StartAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    /// <summary>Stops the loop, awaited, with the host.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) =>
        state.Loop?.StopAsync(cancellationToken) ?? Task.CompletedTask;
    // #endregion keep-warm-wiring

    /// <summary>Releases the loopback client.</summary>
    public void Dispose() => _client?.Dispose();
}
