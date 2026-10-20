// The accounts port over ASP.NET Core Identity's UserManager, which owns the password hashing, the
// normalised lookups and the lockout, over whichever store the request chose.
using Microsoft.AspNetCore.Identity;
using TheYard.Application.Features.Accounts;

namespace TheYard.Infrastructure.Features.Accounts;

// #region identity-account-store
/// <summary>
/// One store's accounts through Identity (ADR: Accounts and per-user bids). The same UserManager
/// serves the relational accounts on one request and the document accounts on the next; this
/// adapter is built per request over the request's one (ADR: The onion cut into vertical slices,
/// rule 7).
/// </summary>
/// <param name="users">Identity's manager over the request's store.</param>
public sealed class IdentityAccountStore(UserManager<YardUser> users) : IAccountStore
{
    /// <summary>The account with this id, or null.</summary>
    /// <param name="id">The account's id.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Account?> FindByIdAsync(string id, CancellationToken cancellation) =>
        Of(await users.FindByIdAsync(id));

    /// <summary>The account under this address, or null.</summary>
    /// <param name="email">The address.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellation) =>
        Of(await users.FindByEmailAsync(email));

    /// <summary>Creates the account, or answers what Identity refused, in a form's words.</summary>
    /// <param name="email">The address, also the user name.</param>
    /// <param name="password">The password.</param>
    /// <param name="createdAtMs">When, milliseconds since the epoch, UTC.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<AccountCreated> CreateAsync(string email, string password, long createdAtMs, CancellationToken cancellation)
    {
        var user = new YardUser { UserName = email, Email = email, CreatedAtMs = createdAtMs };
        var created = await users.CreateAsync(user, password);
        return created.Succeeded ? new AccountCreated(Of(user), AccountProblem.None) : new AccountCreated(null, ProblemOf(created));
    }

    /// <summary>
    /// Asks whether the account is locked before the password is tried, counts a wrong one, which is
    /// the whole lockout mechanism since checking alone does not touch the count, and clears the
    /// count on a right one, so five wrong guesses spread over a week never lock an owner out.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="password">The password tried.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<bool> CheckPasswordAsync(Account account, string password, CancellationToken cancellation)
    {
        if (await users.FindByIdAsync(account.Id) is not { } user || await users.IsLockedOutAsync(user))
        {
            return false;
        }
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return false;
        }
        await users.ResetAccessFailedCountAsync(user);
        return true;
    }

    /// <summary>Moves the security stamp.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task EndSessionsAsync(Account account, CancellationToken cancellation)
    {
        if (await users.FindByIdAsync(account.Id) is { } user)
        {
            await users.UpdateSecurityStampAsync(user);
        }
    }

    /// <summary>Runs the store's password validators over a new password.</summary>
    /// <param name="account">The account.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<AccountProblem> CheckNewPasswordAsync(Account account, string password, CancellationToken cancellation)
    {
        if (await users.FindByIdAsync(account.Id) is not { } user)
        {
            return AccountProblem.BadEmail;
        }
        foreach (var validator in users.PasswordValidators)
        {
            var verdict = await validator.ValidateAsync(users, user, password);
            if (!verdict.Succeeded)
            {
                return ProblemOf(verdict);
            }
        }
        return AccountProblem.None;
    }

    /// <summary>Removes the old password, adds the new one and clears the failed count.</summary>
    /// <param name="account">The account.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<AccountCreated> SetPasswordAsync(Account account, string password, CancellationToken cancellation)
    {
        if (await users.FindByIdAsync(account.Id) is not { } user)
        {
            return new AccountCreated(null, AccountProblem.BadEmail);
        }
        var removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded)
        {
            return new AccountCreated(null, ProblemOf(removed));
        }
        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            return new AccountCreated(null, ProblemOf(added));
        }
        await users.ResetAccessFailedCountAsync(user);
        return new AccountCreated(Of(user), AccountProblem.None);
    }

    private static Account? Of(YardUser? user) =>
        user is null ? null : new Account(user.Id, user.Email ?? "", user.CreatedAtMs, user.SecurityStamp ?? "", user.PasswordHash);

    /// <summary>One problem for every way Identity refuses, rather than its list of codes, which is in the log.</summary>
    private static AccountProblem ProblemOf(IdentityResult result) =>
        result.Errors.Any(error => error.Code.Contains("Password", StringComparison.Ordinal)) ? AccountProblem.PasswordTooShort
        : result.Errors.Any(error => error.Code.Contains("Duplicate", StringComparison.Ordinal)) ? AccountProblem.DuplicateEmail
        : AccountProblem.BadEmail;
}
// #endregion identity-account-store
