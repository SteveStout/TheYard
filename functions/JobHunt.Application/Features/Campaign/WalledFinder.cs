using System.Globalization;
using JobHunt.Application.Shared.Walls;
using JobHunt.Domain.Shared.Clock;
using JobHunt.Domain.Shared.Tracker;
using JobHunt.Domain.Shared.Walls;

namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// A finder behind its wall (jhcode1112 1b). When the finder refuses (a restricted account, a key it does not
/// accept), one bullet goes on dispatch/WALLS.md saying what, the one control, and since when, and every lookup
/// through this wrapper stops for 24 hours after the last refusal without a call, so nothing more is spent on a
/// wall only Steve can clear. After 24 hours one call tries again; an address found takes the bullet away. Both the
/// campaign's address search and the pounce's email leg ask Hunter through this, so they stop together.
/// </summary>
/// <param name="control">The one thing Steve does to clear it, as the bullet says it.</param>
public sealed class WalledFinder(IAddressFinder inner, IMergeStore store, TimeProvider time, string control) : IAddressFinder
{
    /// <summary>How long lookups stop after a refusal.</summary>
    public static readonly TimeSpan Stop = TimeSpan.FromHours(24);

    private string? _walls;

    /// <inheritdoc />
    public string Name => inner.Name;

    /// <summary>The bullet's key on WALLS.md.</summary>
    public string Key => $"{inner.Name}-refused";

    /// <inheritdoc />
    public async Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct)
    {
        _walls ??= await ReadAsync(ct);
        var now = time.GetUtcNow();
        if (WallList.Last(_walls, Key) is { } last && now - last < Stop)
        {
            throw new FinderRefusedException(inner.Name, $"walled until {Central(last + Stop)}", called: false);
        }
        FoundAddress? answer;
        try
        {
            answer = await inner.FindAsync(fullName, domain, ct);
        }
        catch (FinderRefusedException refused) when (refused.Called)
        {
            // Read again right before the write: other lanes add and remove their own bullets in this file.
            _walls = WallList.With(await ReadAsync(ct), Key, Bullet(refused.Reason, now), now);
            await store.WriteAsync(WallList.Path, TrackerCsvText.Encode(_walls), ct);
            throw;
        }
        if (answer is not null && WallList.Last(_walls, Key) is not null)
        {
            _walls = WallList.Without(await ReadAsync(ct), Key);
            await store.WriteAsync(WallList.Path, TrackerCsvText.Encode(_walls), ct);
        }
        return answer;
    }

    private string Bullet(string reason, DateTimeOffset now) =>
        $"{inner.Name} refused the address finder ({reason}): no address is looked up while it stands. "
        + $"Control: {control}. Since {Central(now)}; "
        + "every lookup stops for 24 hours after each refusal, and the first address found removes this bullet.";

    private static string Central(DateTimeOffset at)
    {
        DateTime local = CentralClock.Local(at);
        string zone = local - at.UtcDateTime == TimeSpan.FromHours(-5) ? "CDT" : "CST";
        return local.ToString($"HH:mm '{zone}' ddd M/d", CultureInfo.InvariantCulture);
    }

    private async Task<string> ReadAsync(CancellationToken ct) =>
        await store.ExistsAsync(WallList.Path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(WallList.Path, ct)) : "- none\n";
}
