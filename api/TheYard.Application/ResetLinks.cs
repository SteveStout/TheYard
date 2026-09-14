namespace TheYard.Application;

// A password reset link's public half (ADR: Accounts and per-user bids,
// addendum of 14 September). The link a visitor receives carries a plain
// GUID and nothing else; the signed token that names the account, the
// store and the password's fingerprint is kept on the server under that
// GUID for an hour, and is forgotten the moment it is used. So a link
// reads as a link, the token never travels, an expired GUID names nothing,
// and a second use finds nothing.

// #region port
/// <summary>Port: keep a reset token under a fresh id for a while, hand it back once, then forget it.</summary>
public interface IResetLinks
{
    /// <summary>Keeps the token and returns the id the link will carry: a GUID, nothing derived from the token.</summary>
    Task<string> KeepAsync(string token, TimeSpan lifetime, CancellationToken cancellation);

    /// <summary>The token kept under this id, or null when the id names nothing: never kept, expired, or already used.</summary>
    Task<string?> ReadAsync(string id, CancellationToken cancellation);

    /// <summary>Forgets the id. True when it was there to forget, which is what makes a link one use: two takers, one true.</summary>
    Task<bool> ForgetAsync(string id, CancellationToken cancellation);
}

/// <summary>The id a link carries: a GUID in its plain hyphenated form, so it reads as one.</summary>
public static class ResetLinkIds
{
    public static string Fresh() => Guid.NewGuid().ToString("D");

    /// <summary>Only a GUID is looked up at all; anything else is refused before a store is asked.</summary>
    public static bool IsOne(string? id) => Guid.TryParseExact(id, "D", out _);
}
// #endregion port

// #region memory
/// <summary>
/// The port in memory, for a container with no document store: the test
/// host and a developer's machine. A link minted here works on this process
/// only and does not outlive it, which is the right shape for both.
/// </summary>
public sealed class MemoryResetLinks : IResetLinks
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _kept = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public MemoryResetLinks(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public Task<string> KeepAsync(string token, TimeSpan lifetime, CancellationToken cancellation)
    {
        var now = _clock.GetUtcNow();
        foreach (var (id, entry) in _kept)
        {
            if (entry.Expires <= now)
            {
                _kept.TryRemove(id, out _);
            }
        }

        string fresh = ResetLinkIds.Fresh();
        _kept[fresh] = (token, now.Add(lifetime));
        return Task.FromResult(fresh);
    }

    public Task<string?> ReadAsync(string id, CancellationToken cancellation)
    {
        if (ResetLinkIds.IsOne(id) && _kept.TryGetValue(id, out var entry) && entry.Expires > _clock.GetUtcNow())
        {
            return Task.FromResult<string?>(entry.Token);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<bool> ForgetAsync(string id, CancellationToken cancellation) =>
        Task.FromResult(ResetLinkIds.IsOne(id) && _kept.TryRemove(id, out _));
}
// #endregion memory
