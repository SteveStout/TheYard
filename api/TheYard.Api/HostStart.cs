namespace TheYard.Api;

/// <summary>
/// When this process finished starting: marked once, after the host is built and
/// the default store is warm, and read by the health report's uptime and the
/// Admin tab's startup rows. Registered before the host exists, marked after.
/// </summary>
public sealed class HostStart
{
    /// <summary>The moment the host was marked started; the registration time until then.</summary>
    public DateTimeOffset At { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Marks the process started now.</summary>
    public void Mark() => At = DateTimeOffset.UtcNow;
}
