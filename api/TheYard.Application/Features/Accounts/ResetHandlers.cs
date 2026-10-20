// The reset link: minted for the operator or mailed to an address, and used once, within the hour,
// on the site it was minted for, to choose a new password.
namespace TheYard.Application.Features.Accounts;

// #region reset-handlers
/// <summary>
/// Makes a reset link: a token keyed to the account's current password, kept for the hour under a
/// random id, and the address of the page that uses it (ADR: Accounts and per-user bids, the
/// addendum on what the link looks like). Shared by the two handlers that make one.
/// </summary>
/// <param name="tokens">The signed reset tokens.</param>
/// <param name="links">Where a link's token is kept for the hour.</param>
public sealed class ResetLinkMaker(IResetTokens tokens, IResetLinks links)
{
    /// <summary>A link for this account on this store, from this origin.</summary>
    /// <param name="account">The account.</param>
    /// <param name="store">The store the link works on.</param>
    /// <param name="origin">The site as a visitor reaches it.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string> LinkForAsync(Account account, string store, string origin, CancellationToken cancellation)
    {
        string id = await links.KeepAsync(tokens.Issue(account, store), tokens.Lifetime, cancellation);
        return $"{origin.TrimEnd('/')}/?view=account&reset={id}";
    }

    /// <summary>How long a link works.</summary>
    public TimeSpan Lifetime => tokens.Lifetime;
}

/// <summary>Mints a reset link for an address on the request's store, for the operator to hand over.</summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
/// <param name="links">The link maker.</param>
public sealed class OperatorResetLinkHandler(IAccountStore? store, ResetLinkMaker links)
{
    /// <summary>The account and its link, or why not.</summary>
    /// <param name="email">The address.</param>
    /// <param name="storeKey">The store the request is on.</param>
    /// <param name="origin">The site as a visitor reaches it.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ResetLinkAnswer> HandleAsync(string? email, string storeKey, string origin, CancellationToken cancellation)
    {
        if (store is null)
        {
            return new ResetLinkAnswer(AccountAnswerKind.Unavailable, null, null);
        }
        var account = string.IsNullOrWhiteSpace(email) ? null : await store.FindByEmailAsync(email, cancellation);
        return account is null
            ? new ResetLinkAnswer(AccountAnswerKind.Refused, null, null)
            : new ResetLinkAnswer(AccountAnswerKind.Done, account, await links.LinkForAsync(account, storeKey, origin, cancellation));
    }
}

/// <summary>
/// Mails a reset link to an address, answering the same way whether or not the address has an
/// account here, one email per address per few minutes.
/// </summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
/// <param name="mail">The site's one email.</param>
/// <param name="spacing">The per-address spacing.</param>
/// <param name="links">The link maker.</param>
public sealed class ForgotHandler(IAccountStore? store, IEmailSender mail, IPerAddressAllowance spacing, ResetLinkMaker links)
{
    /// <summary>What a forgot request came to; Done covers both an address with an account and one without.</summary>
    /// <param name="email">The address.</param>
    /// <param name="storeKey">The store the request is on.</param>
    /// <param name="origin">The site as a visitor reaches it.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ForgotAnswer> HandleAsync(string? email, string storeKey, string origin, CancellationToken cancellation)
    {
        if (!mail.Configured)
        {
            return new ForgotAnswer(ForgotAnswerKind.NoMail, mail.Reason);
        }
        if (store is null)
        {
            return new ForgotAnswer(ForgotAnswerKind.Unavailable, null);
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            return new ForgotAnswer(ForgotAnswerKind.NoAddress, null);
        }
        if (spacing.TryTake(email) && await store.FindByEmailAsync(email, cancellation) is { } account)
        {
            string url = await links.LinkForAsync(account, storeKey, origin, cancellation);
            await mail.SendAsync(
                account.Email,
                "Your TheYard password reset link",
                "Somebody asked to reset the password for this address on TheYard. If it was you, open this link within the hour; it works once:\n\n"
                + url
                + "\n\nIf it was not you, nothing has changed and you can ignore this message.",
                cancellation);
        }
        return new ForgotAnswer(ForgotAnswerKind.Done, null);
    }
}

/// <summary>
/// Uses a reset link to choose a new password: the link must be kept, valid, minted on this store
/// and for the password the account has now; the new password must pass the rules; and the link is
/// forgotten before the password changes, so it works once.
/// </summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
/// <param name="tokens">The signed reset tokens.</param>
/// <param name="links">Where a link's token is kept for the hour.</param>
public sealed class ResetPasswordHandler(IAccountStore? store, IResetTokens tokens, IResetLinks links)
{
    /// <summary>The sentence a dead link answers with.</summary>
    public const string NotValid = "That reset link is not valid any more. Ask for a new one.";

    /// <summary>The account with its new password, or why not.</summary>
    /// <param name="linkId">The id the link carries.</param>
    /// <param name="password">The new password.</param>
    /// <param name="storeKey">The store the request is on.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ResetAnswer> HandleAsync(string? linkId, string? password, string storeKey, CancellationToken cancellation)
    {
        if (store is null)
        {
            return new ResetAnswer(ResetAnswerKind.Unavailable, null, null, AccountProblem.None);
        }
        string? kept = string.IsNullOrWhiteSpace(linkId) ? null : await links.ReadAsync(linkId, cancellation);
        if (await tokens.ReadAsync(kept) is not { } claims)
        {
            return ResetAnswer.LinkRefused(NotValid);
        }
        if (!string.Equals(claims.Store, storeKey, StringComparison.Ordinal))
        {
            return ResetAnswer.LinkRefused("That reset link belongs to the other site. Open it there.");
        }
        var account = await store.FindByIdAsync(claims.UserId, cancellation);
        if (account is null || tokens.Fingerprint(account.PasswordHash) != claims.Fingerprint)
        {
            return ResetAnswer.LinkRefused(NotValid);
        }
        if (string.IsNullOrEmpty(password))
        {
            return new ResetAnswer(ResetAnswerKind.PasswordRefused, null, "Choose a new password of eight characters or more.", AccountProblem.None);
        }
        if (await store.CheckNewPasswordAsync(account, password, cancellation) is not AccountProblem.None and var problem)
        {
            return new ResetAnswer(ResetAnswerKind.PasswordRefused, null, null, problem);
        }
        // Forgotten before the change, so a second use of the same link finds nothing.
        if (!await links.ForgetAsync(linkId!, cancellation))
        {
            return ResetAnswer.LinkRefused(NotValid);
        }
        var changed = await store.SetPasswordAsync(account, password, cancellation);
        return changed.Account is { } after
            ? new ResetAnswer(ResetAnswerKind.Changed, after, null, AccountProblem.None)
            : new ResetAnswer(ResetAnswerKind.PasswordRefused, null, null, changed.Problem);
    }
}
// #endregion reset-handlers

/// <summary>An operator's reset link, or why not.</summary>
/// <param name="Kind">Done, Unavailable, or Refused for no such account.</param>
/// <param name="Account">The account.</param>
/// <param name="Url">The link.</param>
public sealed record ResetLinkAnswer(AccountAnswerKind Kind, Account? Account, string? Url);

/// <summary>What a forgot request came to.</summary>
public enum ForgotAnswerKind
{
    /// <summary>Answered the same way whether or not the address has an account.</summary>
    Done,

    /// <summary>This site cannot send email.</summary>
    NoMail,

    /// <summary>The request's store keeps no accounts.</summary>
    Unavailable,

    /// <summary>No address was given.</summary>
    NoAddress,
}

/// <summary>A forgot request's answer.</summary>
/// <param name="Kind">What it came to.</param>
/// <param name="Reason">Why there is no email, for NoMail.</param>
public sealed record ForgotAnswer(ForgotAnswerKind Kind, string? Reason);

/// <summary>What a reset came to.</summary>
public enum ResetAnswerKind
{
    /// <summary>The password changed.</summary>
    Changed,

    /// <summary>The request's store keeps no accounts.</summary>
    Unavailable,

    /// <summary>The link did not work.</summary>
    LinkRefused,

    /// <summary>The new password was refused.</summary>
    PasswordRefused,
}

/// <summary>A reset's answer.</summary>
/// <param name="Kind">What it came to.</param>
/// <param name="Account">The account with its new password.</param>
/// <param name="Detail">The sentence for a refused link or an empty password.</param>
/// <param name="Problem">What the rules said about the new password.</param>
public sealed record ResetAnswer(ResetAnswerKind Kind, Account? Account, string? Detail, AccountProblem Problem)
{
    /// <summary>A link that did not work.</summary>
    /// <param name="detail">Why.</param>
    public static ResetAnswer LinkRefused(string detail) => new(ResetAnswerKind.LinkRefused, null, detail, AccountProblem.None);
}
