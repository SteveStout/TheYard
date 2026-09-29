namespace TheYard.Api;

/// <summary>
/// The keep-warm loop, when this container runs one (ADR: Kept awake). Built after
/// the host is, because it dials the host's own address, and read by the health
/// report; empty where the loop is off, the test host included.
/// </summary>
public sealed class KeepWarmState
{
    /// <summary>The loop, or null where it is off.</summary>
    public KeepWarm? Loop { get; set; }
}
