// What the Accounts slice asks of the world: the accounts on the request's store, the hour's
// allowance of new accounts, the per-address spacing of reset emails, and the signed reset tokens.
namespace TheYard.Application.Features.Accounts;

// #region account-ports
/// <summary>
/// The accounts on one store (ADR: The onion cut into vertical slices, rule 7). Identity owns the
/// password hashing, the normalised lookups and the lockout behind this port, in the Infrastructure
/// ring; a request on a store that keeps no accounts has no store at all, and every handler answers
/// that as unavailable.
/// </summary>
public interface IAccountStore : IAccountReader
{
    /// <summary>Creates an account, or says what was wrong with the address or the password.</summary>
    Task<AccountCreated> CreateAsync(string email, string password, long createdAtMs, CancellationToken cancellation);

    /// <summary>
    /// True when the password opens the account. A locked account answers false without the
    /// password being tried, a wrong one counts towards the lockout, and a right one clears the count.
    /// </summary>
    Task<bool> CheckPasswordAsync(Account account, string password, CancellationToken cancellation);

    /// <summary>Moves the account's security stamp, so every session it had open is refused on its next request.</summary>
    Task EndSessionsAsync(Account account, CancellationToken cancellation);

    /// <summary>What the store's password rules say about a new password, or <see cref="AccountProblem.None"/>.</summary>
    Task<AccountProblem> CheckNewPasswordAsync(Account account, string password, CancellationToken cancellation);

    /// <summary>Replaces the account's password and clears its failed count; the account as it is afterwards, or the problem.</summary>
    Task<AccountCreated> SetPasswordAsync(Account account, string password, CancellationToken cancellation);
}

/// <summary>
/// The read side of the accounts: an account by id or by address, and nothing that writes or counts a
/// failed password (ADR-115, Interface Segregation). The one query, who am I, takes this.
/// </summary>
public interface IAccountReader
{
    /// <summary>The account with this id, or null.</summary>
    Task<Account?> FindByIdAsync(string id, CancellationToken cancellation);

    /// <summary>The account under this address, or null.</summary>
    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellation);
}

/// <summary>An allowance that is taken and can be given back: the hour's new accounts, for the whole site.</summary>
public interface IAllowance
{
    /// <summary>Takes one, or false when none is left.</summary>
    bool TryTake();

    /// <summary>Gives back one taken for something that was never made.</summary>
    void GiveBack();
}

/// <summary>A spacing per address: one reset email per address per few minutes.</summary>
public interface IPerAddressAllowance
{
    /// <summary>True when this address may be mailed now.</summary>
    bool TryTake(string address);
}

/// <summary>The signed reset tokens a reset link carries, issued against the password they replace.</summary>
public interface IResetTokens
{
    /// <summary>How long a reset link works.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>A token for this account on this store, keyed to its current password.</summary>
    string Issue(Account account, string store);

    /// <summary>The claims a token carries, or null for a token that is not valid.</summary>
    Task<ResetClaims?> ReadAsync(string? token);

    /// <summary>The fingerprint of a password hash, which a token is compared with.</summary>
    string Fingerprint(string? passwordHash);
}
// #endregion account-ports

/// <summary>An account, as the slice needs it.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="Email">Its address.</param>
/// <param name="CreatedAtMs">When it was created, milliseconds since the epoch, UTC; null for an account older than the column.</param>
/// <param name="SecurityStamp">The stamp a session's token is issued against.</param>
/// <param name="PasswordHash">The current password's hash, which a reset token is issued against.</param>
public sealed record Account(string Id, string Email, long? CreatedAtMs, string SecurityStamp, string? PasswordHash);

/// <summary>What was wrong with an address or a password, in the words a form needs.</summary>
public enum AccountProblem
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The password is shorter than the rules allow.</summary>
    PasswordTooShort,

    /// <summary>There is already an account with the address.</summary>
    DuplicateEmail,

    /// <summary>The address does not look like one.</summary>
    BadEmail,
}

/// <summary>An account made or changed, or the problem that stopped it.</summary>
/// <param name="Account">The account, or null.</param>
/// <param name="Problem">What stopped it, or None.</param>
public sealed record AccountCreated(Account? Account, AccountProblem Problem);

/// <summary>What a reset token carries.</summary>
/// <param name="UserId">The account.</param>
/// <param name="Store">The store the link was minted on.</param>
/// <param name="Fingerprint">The fingerprint of the password it replaces.</param>
public sealed record ResetClaims(string UserId, string Store, string Fingerprint);
