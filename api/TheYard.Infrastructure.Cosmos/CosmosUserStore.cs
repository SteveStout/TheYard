using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Azure.Cosmos;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// ASP.NET Core Identity over one document per account, plus one claim
/// document per email address (ADR: Accounts on a document store).
///
/// <para>It implements the five store interfaces this application uses,
/// password, email, lockout, security stamp and the base, and none of the ones
/// it does not: no roles, claims, logins or tokens, which is most of what the
/// relational shape carries and none of what a sign-in reads.</para>
///
/// <para>Every operation is a point read or a point write. Sign-in is two reads,
/// the claim by address and then the account by id; "who am I" is one; a failed
/// password is one replace. Nothing here queries.</para>
/// </summary>
public sealed class CosmosUserStore(CosmosStore store) :
    IUserStore<YardUser>,
    IUserPasswordStore<YardUser>,
    IUserEmailStore<YardUser>,
    IUserLockoutStore<YardUser>,
    IUserSecurityStampStore<YardUser>
{
    private const string Partition = "pinned to the account";
    private const string ClaimPartition = "pinned to the address";
    private static readonly IdentityErrorDescriber Errors = new();

    // #region create
    /// <summary>
    /// The claim first, the account second. A store with no unique index across
    /// partitions cannot refuse a second account on the same address; a create
    /// of a document whose id is the address can, with 409, and that refusal is
    /// what makes the address unique. If the account write then fails, the
    /// claim is removed again, so a failure leaves nothing behind. A process
    /// that dies between the two writes leaves a claim with no account, which
    /// is the honest cost of the shape and is written down in the record.
    /// </summary>
    public async Task<IdentityResult> CreateAsync(YardUser user, CancellationToken cancellationToken)
    {
        string normalizedEmail = user.NormalizedEmail ?? user.NormalizedUserName
            ?? throw new InvalidOperationException("an account needs a normalized email address before it is stored");
        var claim = new EmailClaimDocument { Id = EmailClaimDocument.IdFor(normalizedEmail), UserId = user.Id };
        try
        {
            await store.CreateAsync(store.Users, claim, claim.Id, ClaimPartition, "CreateItem (email claim)");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return IdentityResult.Failed(Errors.DuplicateEmail(user.Email ?? ""));
        }

        try
        {
            var response = await store.CreateAsync(store.Users, ToDocument(user), user.Id, Partition, "CreateItem (account)");
            user.ConcurrencyStamp = response.ETag;
            return IdentityResult.Success;
        }
        catch (CosmosException)
        {
            await TryDeleteClaimAsync(claim.Id);
            throw;
        }
    }
    // #endregion create

    // #region update
    /// <summary>
    /// A replace that carries the etag the account was read with. Identity keeps
    /// that in ConcurrencyStamp, which on the relational side is a GUID the
    /// framework rotates and here is the document's own etag; a stale one is
    /// refused with 412, which Identity reports as a concurrency failure
    /// exactly as it would a row that moved under it.
    /// </summary>
    public async Task<IdentityResult> UpdateAsync(YardUser user, CancellationToken cancellationToken)
    {
        try
        {
            var response = await store.ReplaceAsync(store.Users, ToDocument(user), user.Id, user.Id, user.ConcurrencyStamp, Partition);
            user.ConcurrencyStamp = response.ETag;
            return IdentityResult.Success;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return IdentityResult.Failed(Errors.ConcurrencyFailure());
        }
    }
    // #endregion update

    public async Task<IdentityResult> DeleteAsync(YardUser user, CancellationToken cancellationToken)
    {
        await store.DeleteAsync<UserDocument>(store.Users, user.Id, user.Id, Partition);
        if (user.NormalizedEmail is { } normalized)
        {
            await TryDeleteClaimAsync(EmailClaimDocument.IdFor(normalized));
        }
        // Not the bids. There is no cascade on this side: a bid is a document
        // in another container partitioned on the buyer, and taking it with
        // the account would be a second container's batch, which the record
        // lists as a cost of the document model rather than doing quietly.
        return IdentityResult.Success;
    }

    private async Task TryDeleteClaimAsync(string claimId)
    {
        try
        {
            await store.DeleteAsync<EmailClaimDocument>(store.Users, claimId, claimId, ClaimPartition);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone, which is the state wanted.
        }
    }

    // #region find
    public async Task<YardUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var document = await store.ReadAsync<UserDocument>(store.Users, userId, userId, Partition);
        return document is null ? null : ToUser(document);
    }

    /// <summary>
    /// The address first, then the account: two point reads, about a request
    /// unit each, against the one cross-partition query the relational shape
    /// would run. This application registers the address as the user name, so
    /// a lookup by normalized name is the same lookup.
    /// </summary>
    public async Task<YardUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        string claimId = EmailClaimDocument.IdFor(normalizedEmail);
        var claim = await store.ReadAsync<EmailClaimDocument>(store.Users, claimId, claimId, ClaimPartition);
        return claim is null ? null : await FindByIdAsync(claim.UserId, cancellationToken);
    }

    public Task<YardUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        FindByEmailAsync(normalizedUserName, cancellationToken);
    // #endregion find

    // #region mapping
    private static UserDocument ToDocument(YardUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = user.EmailConfirmed,
        PasswordHash = user.PasswordHash,
        SecurityStamp = user.SecurityStamp,
        LockoutEnd = user.LockoutEnd,
        LockoutEnabled = user.LockoutEnabled,
        AccessFailedCount = user.AccessFailedCount,
        CreatedAtMs = user.CreatedAtMs,
    };

    private static YardUser ToUser(UserDocument d) => new()
    {
        Id = d.Id,
        UserName = d.UserName,
        NormalizedUserName = d.NormalizedUserName,
        Email = d.Email,
        NormalizedEmail = d.NormalizedEmail,
        EmailConfirmed = d.EmailConfirmed,
        PasswordHash = d.PasswordHash,
        SecurityStamp = d.SecurityStamp,
        LockoutEnd = d.LockoutEnd,
        LockoutEnabled = d.LockoutEnabled,
        AccessFailedCount = d.AccessFailedCount,
        CreatedAtMs = d.CreatedAtMs,
        // The etag rides in the concurrency stamp so UpdateAsync can send it back.
        ConcurrencyStamp = d.ETag,
    };
    // #endregion mapping

    // The rest is Identity's contract for reading and writing the object in
    // memory. Nothing below touches the store; the persistence is above.
    public Task<string> GetUserIdAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(YardUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(YardUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }

    public Task SetPasswordHashAsync(YardUser user, string? passwordHash, CancellationToken cancellationToken) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetEmailAsync(YardUser user, string? email, CancellationToken cancellationToken) { user.Email = email; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
    public Task<bool> GetEmailConfirmedAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
    public Task SetEmailConfirmedAsync(YardUser user, bool confirmed, CancellationToken cancellationToken) { user.EmailConfirmed = confirmed; return Task.CompletedTask; }
    public Task<string?> GetNormalizedEmailAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(YardUser user, string? normalizedEmail, CancellationToken cancellationToken) { user.NormalizedEmail = normalizedEmail; return Task.CompletedTask; }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(YardUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) { user.LockoutEnd = lockoutEnd; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(YardUser user, CancellationToken cancellationToken) { user.AccessFailedCount++; return Task.FromResult(user.AccessFailedCount); }
    public Task ResetAccessFailedCountAsync(YardUser user, CancellationToken cancellationToken) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(YardUser user, bool enabled, CancellationToken cancellationToken) { user.LockoutEnabled = enabled; return Task.CompletedTask; }

    public Task SetSecurityStampAsync(YardUser user, string stamp, CancellationToken cancellationToken) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(YardUser user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);

    public void Dispose()
    {
        // Nothing to release: the client is the store's and lives as long as the process.
    }
}
