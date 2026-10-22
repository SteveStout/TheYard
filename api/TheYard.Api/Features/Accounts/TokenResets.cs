// The reset token port over the session issuer, which signs reset tokens with the same key and a
// fingerprint of the password they replace.
using TheYard.Application.Features.Accounts;

namespace TheYard.Api.Features.Accounts;

/// <summary>The signed reset tokens a reset link carries (Tokens.cs), as the Accounts slice's port asks for them.</summary>
/// <param name="issuer">The session issuer.</param>
public sealed class TokenResets(TokenIssuer issuer) : IResetTokens
{
    /// <summary>An hour.</summary>
    public TimeSpan Lifetime => TokenIssuer.ResetLifetime;

    /// <summary>A token for this account on this store, keyed to its current password.</summary>
    /// <param name="account">The account.</param>
    /// <param name="store">The store the link works on.</param>
    public string Issue(Account account, string store) => issuer.IssueReset(account.Id, store, account.PasswordHash);

    /// <summary>The claims a token carries, or null for one that is not valid.</summary>
    /// <param name="token">The token.</param>
    public async Task<ResetClaims?> ReadAsync(string? token) =>
        await issuer.ReadResetAsync(token) is { } claims ? new ResetClaims(claims.UserId, claims.Store, claims.Fingerprint) : null;

    /// <summary>The fingerprint of a password hash.</summary>
    /// <param name="passwordHash">The hash.</param>
    public string Fingerprint(string? passwordHash) => TokenIssuer.Fingerprint(passwordHash);
}
