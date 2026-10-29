namespace TheYard.Api.Shared.Observability;

/// <summary>
/// The two error rings the Admin tab merges: the server's fifty and the
/// browser's fifty, kept apart so a flood of anonymous browser reports can only
/// push out other browser reports (the two-rings region in Program.cs).
/// </summary>
/// <param name="clock">The one clock both rings stamp an entry with.</param>
public sealed class ErrorRings(TimeProvider clock)
{
    /// <summary>Errors the server answered or threw.</summary>
    public ErrorRingBuffer Server { get; } = new(50, clock);

    /// <summary>Errors the page caught and reported.</summary>
    public ErrorRingBuffer Browser { get; } = new(50, clock);
}
