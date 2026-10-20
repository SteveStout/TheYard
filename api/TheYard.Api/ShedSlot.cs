// The Shed this process started, kept where the service that stops it can find it.
namespace TheYard.Api;

/// <summary>
/// The started Shed, or nothing: filled when The Shed is mounted (Shared/ShedMount.cs) and
/// emptied when the host stops (Shared/Hosted/ShedStopper.cs), which disposes it.
/// </summary>
public sealed class ShedSlot
{
    private ShedApplication? _shed;

    /// <summary>The running Shed, or null when none was started or it has been stopped.</summary>
    public ShedApplication? Shed => Volatile.Read(ref _shed);

    /// <summary>Keeps the started Shed.</summary>
    /// <param name="shed">The Shed that started.</param>
    public void Hold(ShedApplication shed) => Volatile.Write(ref _shed, shed);

    /// <summary>Stops and releases the Shed, once; a second call finds nothing.</summary>
    public ValueTask ReleaseAsync() =>
        Interlocked.Exchange(ref _shed, null) is { } shed ? shed.DisposeAsync() : ValueTask.CompletedTask;
}
