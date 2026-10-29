namespace TheYard.Api.Shared.Instance;

/// <summary>
/// When this process finished starting: marked once, after the host is built and
/// the default store is warm, and read by the health report's uptime and the
/// Admin tab's startup rows. Registered before the host exists, marked after.
/// </summary>
/// <param name="clock">The one clock the host is handed, so a test can say when this process started.</param>
public sealed class HostStart(TimeProvider clock)
{
    /// <summary>The moment the host was marked started; the registration time until then.</summary>
    public DateTimeOffset At { get; private set; } = clock.GetUtcNow();

    /// <summary>Marks the process started now.</summary>
    public void Mark() => At = clock.GetUtcNow();
}
