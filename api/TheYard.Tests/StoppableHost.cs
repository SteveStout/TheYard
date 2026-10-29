using Microsoft.Extensions.Hosting;

namespace TheYard.Tests;

/// <summary>
/// A host lifetime a test can stop: the three signals <see cref="IHostApplicationLifetime"/>
/// carries, and a <see cref="Stop"/> that cancels the stopping one.
///
/// <para>It exists because the work this codebase starts and forgets takes the host's stopping
/// token rather than a request's, and a test of that cannot hand in a token nothing owns: it has
/// to hold the thing the token came from and cancel it, or the test is asserting on the shape of
/// a signature instead of on what the signature buys.</para>
/// </summary>
public sealed class StoppableHost : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();
    private readonly CancellationTokenSource _started = new();

    /// <summary>Cancelled once the host has started; cancelled from the start here.</summary>
    public CancellationToken ApplicationStarted => _started.Token;

    /// <summary>Cancelled when the host begins to stop, which is what <see cref="Stop"/> does.</summary>
    public CancellationToken ApplicationStopping => _stopping.Token;

    /// <summary>Cancelled once the host has stopped.</summary>
    public CancellationToken ApplicationStopped => _stopped.Token;

    /// <summary>Makes one, started.</summary>
    public StoppableHost() => _started.Cancel();

    /// <summary>Begins the stop, which cancels <see cref="ApplicationStopping"/>.</summary>
    public void Stop() => _stopping.Cancel();

    /// <summary>What the platform calls to ask for a stop; the same thing here.</summary>
    public void StopApplication() => Stop();

    /// <summary>Releases the three sources.</summary>
    public void Dispose()
    {
        _stopping.Dispose();
        _stopped.Dispose();
        _started.Dispose();
    }
}
