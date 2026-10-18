// How often one account may run the deliberate failure. Its own file because the endpoint that
// throws on purpose is the one route whose every call is meant to land in the error ring, and the
// limit on it is what keeps that ring the operator's.
namespace TheYard.Api;

/// <summary>
/// One self-test per account per minute, held in memory: the ring it protects is in memory too,
/// so a limit that outlived it would protect nothing. The clock is the host's one TimeProvider.
/// </summary>
public sealed class SelfTestCooldown(TimeProvider clock)
{
    /// <summary>How long an account waits between self-tests.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>The lock around the last-run times.</summary>
    private readonly object _gate = new();

    /// <summary>When each account last ran it.</summary>
    private readonly Dictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);

    /// <summary>True, and the time recorded, when this account has not run it inside the last minute.</summary>
    /// <param name="account">The signed-in account's id.</param>
    public bool TryTake(string account)
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_last.TryGetValue(account, out var at) && now - at < Every)
            {
                return false;
            }
            _last[account] = now;
            return true;
        }
    }
}
