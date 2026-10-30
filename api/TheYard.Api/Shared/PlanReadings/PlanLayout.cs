// Which plan this site is on and which apps share it, kept for an hour. Its own file because
// finding the layout and deciding how long that answer is good for is a different job from asking
// Azure Monitor for a minute of readings, which is PlanReader.cs; this is the only part of the
// concern that remembers anything, so it is the only part that needs a clock.
namespace TheYard.Api.Shared.PlanReadings;

/// <summary>
/// The plan this site runs on and every app on it, read off the site itself rather than configured,
/// so the readings follow the site if it is ever moved to another plan.
///
/// <para>The answer is kept for an hour by the clock it was handed, because apps are added to a
/// plan about never and the minute read asks for it every minute. A caller that needs the layout as
/// it is right now asks fresh, which the hour read does: it is the read that would notice an app
/// appearing.</para>
/// </summary>
/// <param name="selfResourceId">This site's own resource path, which the plan is read off.</param>
/// <param name="clock">The clock the keep ages by, handed in so a test can move it (ADR: Onion and SOLID, the addendum on the clock).</param>
public sealed class PlanLayout(string selfResourceId, TimeProvider clock)
{
    /// <summary>How long one layout read is good for.</summary>
    public static readonly TimeSpan GoodFor = TimeSpan.FromHours(1);

    /// <summary>The last layout read and when it was read, or null before the first read.</summary>
    private (DateTimeOffset At, string PlanId, IReadOnlyList<string> Apps)? _known;

    /// <summary>True when this site's own path is known, which is what the layout is read off.</summary>
    public bool SiteKnown => !string.IsNullOrWhiteSpace(selfResourceId);

    /// <summary>The plan's resource name once it has been read; null before the first read.</summary>
    public string? PlanName => _known is { } known ? PlanReader.NameOf(known.PlanId) : null;

    /// <summary>
    /// The plan's id and the ids of every app on it: the kept answer while it is younger than
    /// <see cref="GoodFor"/> on the handed clock, and otherwise a fresh pair of reads.
    /// </summary>
    /// <param name="token">A management token, acquired by the caller that already needed one.</param>
    /// <param name="cancellation">Cancelled when whatever asked for this reading has gone.</param>
    /// <param name="fresh">True to read again however young the kept answer is.</param>
    public async Task<(string PlanId, IReadOnlyList<string> Apps)> OfAsync(string token, CancellationToken cancellation, bool fresh = false)
    {
        if (!fresh && _known is { } known && clock.GetUtcNow() - known.At < GoodFor)
        {
            return (known.PlanId, known.Apps);
        }

        using var site = await PlanArm.GetAsync(token, selfResourceId + "?api-version=2023-12-01", cancellation);
        string planId = site.RootElement.GetProperty("properties").GetProperty("serverFarmId").GetString()!;
        using var listed = await PlanArm.GetAsync(token, planId + "/sites?api-version=2023-12-01", cancellation);
        var apps = listed.RootElement.GetProperty("value").EnumerateArray()
            .Select(app => app.GetProperty("id").GetString()!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _known = (clock.GetUtcNow(), planId, apps);
        return (planId, apps);
    }
}
