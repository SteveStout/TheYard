// Application Insights, read back for the Admin tab. This file holds the reader: whether this
// build can read anything, how long one answer is good for, and the one read the card calls. The
// other parts live beside it: Telemetry.Source.cs (the token, the query and the POST that asks),
// Telemetry.Kql.cs (the query and the site filter it carries) and Telemetry.Shape.cs (turning the
// query's table into the pieces the card renders).
namespace TheYard.Api.Shared.Observability;

/// <summary>
/// Application Insights, read back (ADR-024). The API sends telemetry with the
/// Azure Monitor OpenTelemetry distro; this type is the other direction, so the
/// Admin tab can show what the running app has been reporting about itself.
///
/// <para>Its one job is how long an answer is good for. The asking is
/// <see cref="TelemetrySource"/>, which is handed in, so what is left here is a minute of cache
/// and the question of whether this build is wired to read at all.</para>
/// </summary>
/// <param name="source">The component, asked for the last hour.</param>
/// <param name="enabled">True only where telemetry is actually wired, which is the deployed container.</param>
/// <param name="clock">The clock the cached answer ages by, handed in so a test can move it.</param>
public sealed class TelemetryReader(TelemetrySource source, bool enabled, TimeProvider clock)
{
    /// <summary>
    /// How long one answer is served again for. The Admin tab is a page someone leaves open, and
    /// the query costs a token exchange plus a round trip.
    /// </summary>
    public static readonly TimeSpan GoodFor = TimeSpan.FromSeconds(60);

    /// <summary>The lock that guards the cached answer, since several requests can read and fill it at once.</summary>
    private readonly object _gate = new();

    /// <summary>The last answer, success or failure, served again until it is a minute old.</summary>
    private object? _cached;

    /// <summary>When the cached answer was read, on the handed clock.</summary>
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// True only where telemetry is actually wired, which is the deployed
    /// container. The component's app id is not the test: it has a default,
    /// so reading it alone would make this always true, the unconfigured path
    /// dead code, and every local request an eight-second wait on a metadata
    /// endpoint that only exists in Azure.
    /// </summary>
    public bool Configured => enabled && source.ComponentNamed;

    // #region read
    /// <summary>
    /// The last hour, as the Admin tab shows it: the kept answer while it is younger than
    /// <see cref="GoodFor"/> on the handed clock, and otherwise one read of the component.
    ///
    /// <para>Failures are kept like successes. Without that, a component that has gone unreachable
    /// costs a full timeout on every request to a public, unauthenticated endpoint, and the answer
    /// is the same every time anyway.</para>
    /// </summary>
    /// <param name="cancellation">Cancelled when the poll that asked has gone.</param>
    public async Task<object> GetRecentAsync(CancellationToken cancellation)
    {
        if (!Configured)
        {
            return new
            {
                configured = false,
                note = "Application Insights is not configured for this build. It is wired at deploy time from Azure; a local run reports nothing.",
            };
        }
        lock (_gate)
        {
            if (_cached is not null && clock.GetUtcNow() - _cachedAt < GoodFor)
            {
                return _cached;
            }
        }

        return Keep(await source.AskAsync(cancellation));
    }
    // #endregion read

    /// <summary>Holds one answer, with the moment the handed clock reads, and hands it straight back.</summary>
    private object Keep(object state)
    {
        lock (_gate)
        {
            _cached = state;
            _cachedAt = clock.GetUtcNow();
        }
        return state;
    }
}
