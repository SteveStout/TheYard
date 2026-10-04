// The password reset as one flow: minting a link for an account, and using a link to choose a
// new password. The account handlers only read the request and write the answer; the steps and
// their order live here, so the rules a reset follows read in one place. It stays in the host because it works through Identity's UserManager, which the
// Application ring does not reference.
using Microsoft.AspNetCore.Identity;
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>What a reset attempt ended in: the account signed back in, or the title and sentence a refusal shows.</summary>
/// <param name="User">The account whose password changed, or null when the reset was refused.</param>
/// <param name="Title">The problem's title when refused; empty when the password changed.</param>
/// <param name="Detail">The sentence a refusal shows; empty when the password changed.</param>
public sealed record ResetResult(YardUser? User, string Title, string Detail)
{
    /// <summary>The password changed on this account.</summary>
    public static ResetResult Changed(YardUser user) => new(user, "", "");

    /// <summary>The link was refused, with one sentence for every way it can be wrong.</summary>
    public static ResetResult LinkRefused(string detail) => new(null, "The link did not work", detail);

    /// <summary>The link was good and the new password was not.</summary>
    public static ResetResult PasswordRefused(string detail) => new(null, "The password was not changed", detail);
}

/// <summary>
/// Mints reset links and spends them. A link carries a GUID and nothing else; the signed token
/// that names the account, the store and a fingerprint of the password hash is kept under that
/// GUID for an hour and forgotten on use, so a link works once.
/// </summary>
public sealed class PasswordReset(TokenIssuer issuer, IResetLinks links)
{
    /// <summary>One sentence for every way a link can be wrong: expired, used, forged, or naming nothing.</summary>
    public const string NotValid = "That reset link is not valid any more. Ask for a new one.";

    // #region mint
    /// <summary>
    /// A reset link for this account on this store, on the site the visitor reaches. The origin is
    /// worked out by the caller from the site's configured address, because the request's own
    /// host behind the edge is the origin server, and that is not an address anybody should be sent.
    /// </summary>
    public async Task<string> LinkForAsync(YardUser user, string store, string origin, CancellationToken cancellation)
    {
        string token = issuer.IssueReset(user.Id, store, user.PasswordHash);
        string id = await links.KeepAsync(token, TokenIssuer.ResetLifetime, cancellation);
        return $"{origin.TrimEnd('/')}/?view=account&reset={id}";
    }
    // #endregion mint

    // #region spend
    /// <summary>
    /// Uses a link to set a new password. The steps run in this order so that nothing is lost on
    /// a refusal: the link is read and checked, the new password is validated, the link is spent,
    /// and only then is the old password replaced. A second taker of the same link finds nothing
    /// to spend and is refused before the password moves.
    /// </summary>
    public async Task<ResetResult> ResetAsync(UserManager<YardUser> users, string? linkId, string? password, string store, CancellationToken cancellation)
    {
        string? kept = string.IsNullOrWhiteSpace(linkId) ? null : await links.ReadAsync(linkId, cancellation);
        if (await issuer.ReadResetAsync(kept) is not { } claims)
        {
            return ResetResult.LinkRefused(NotValid);
        }
        if (!string.Equals(claims.Store, store, StringComparison.Ordinal))
        {
            return ResetResult.LinkRefused("That reset link belongs to the other site. Open it there.");
        }

        // The fingerprint is of the password hash the link was minted against, so a link stops
        // working the moment the password changes by any route.
        var user = await users.FindByIdAsync(claims.UserId);
        if (user is null || TokenIssuer.Fingerprint(user.PasswordHash) != claims.Fingerprint)
        {
            return ResetResult.LinkRefused(NotValid);
        }
        if (string.IsNullOrEmpty(password))
        {
            return ResetResult.PasswordRefused("Choose a new password of eight characters or more.");
        }

        // Validated before anything is removed, so a refused password leaves the old one in place
        // rather than an account with none.
        foreach (var validator in users.PasswordValidators)
        {
            var verdict = await validator.ValidateAsync(users, user, password);
            if (!verdict.Succeeded)
            {
                return ResetResult.PasswordRefused(Accounts.Explain(verdict));
            }
        }

        if (!await links.ForgetAsync(linkId!, cancellation))
        {
            return ResetResult.LinkRefused(NotValid);
        }

        // Removed then added rather than reset through a token provider, because a provider needs
        // a key ring this container does not keep across a roll. Both calls update the security
        // stamp.
        var removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded)
        {
            return ResetResult.PasswordRefused(Accounts.Explain(removed));
        }
        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            return ResetResult.PasswordRefused(Accounts.Explain(added));
        }

        await users.ResetAccessFailedCountAsync(user);
        return ResetResult.Changed(user);
    }
    // #endregion spend
}
