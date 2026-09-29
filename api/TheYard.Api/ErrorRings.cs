namespace TheYard.Api;

/// <summary>
/// The two error rings the Admin tab merges: the server's fifty and the
/// browser's fifty, kept apart so a flood of anonymous browser reports can only
/// push out other browser reports (the two-rings region in Program.cs).
/// </summary>
public sealed class ErrorRings
{
    /// <summary>Errors the server answered or threw.</summary>
    public ErrorRingBuffer Server { get; } = new(50);

    /// <summary>Errors the page caught and reported.</summary>
    public ErrorRingBuffer Browser { get; } = new(50);
}
