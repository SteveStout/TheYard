// Register, sign in, sign out and who am I: the four handlers that open, close and read a session.
// The cookie is the host's; these decide whether there is an account to put in it.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Accounts;

// #region session-handlers
/// <summary>
/// Creates an account on the request's store within the hour's allowance (ADR: The one write a
/// stranger can make). The allowance is taken after the request is read and before the password is
/// hashed, and given back when nothing was made.
/// </summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
/// <param name="allowance">The hour's allowance of new accounts.</param>
/// <param name="nowMs">The server's clock, in milliseconds since the epoch.</param>
public sealed class RegisterHandler(IAccountStore? store, IAllowance allowance, Func<long> nowMs) : ICommandHandler<RegisterCommand, AccountAnswer>
{
    /// <summary>Answers one registration.</summary>
    /// <param name="command">The address and the password.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<AccountAnswer> HandleAsync(RegisterCommand command, CancellationToken cancellation)
    {
        var (email, password) = command;
        if (store is null)
        {
            return AccountAnswer.Of(AccountAnswerKind.Unavailable);
        }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return AccountAnswer.Of(AccountAnswerKind.Unreadable);
        }
        if (!allowance.TryTake())
        {
            return AccountAnswer.Of(AccountAnswerKind.TooMany);
        }
        var created = await store.CreateAsync(email, password, nowMs(), cancellation);
        if (created.Account is null)
        {
            // Nothing was made, so nothing was spent.
            allowance.GiveBack();
            return new AccountAnswer(AccountAnswerKind.Refused, null, created.Problem);
        }
        return new AccountAnswer(AccountAnswerKind.Done, created.Account, AccountProblem.None);
    }
}

/// <summary>One registration: an address and a password.</summary>
/// <param name="Email">The address.</param>
/// <param name="Password">The password.</param>
public sealed record RegisterCommand(string? Email, string? Password);

/// <summary>
/// Signs in with an address and a password. Every way that can fail is one answer, so a reply never
/// tells a stranger which addresses are registered here.
/// </summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
public sealed class SignInHandler(IAccountStore? store) : ICommandHandler<SignInCommand, AccountAnswer>
{
    /// <summary>Answers one sign-in.</summary>
    /// <param name="command">The address and the password.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<AccountAnswer> HandleAsync(SignInCommand command, CancellationToken cancellation)
    {
        var (email, password) = command;
        if (store is null)
        {
            return AccountAnswer.Of(AccountAnswerKind.Unavailable);
        }
        var account = email is null ? null : await store.FindByEmailAsync(email, cancellation);
        if (account is null || password is null || !await store.CheckPasswordAsync(account, password, cancellation))
        {
            return AccountAnswer.Of(AccountAnswerKind.Refused);
        }
        return new AccountAnswer(AccountAnswerKind.Done, account, AccountProblem.None);
    }
}

/// <summary>One sign-in: an address and a password. It opens a session, so it is a command.</summary>
/// <param name="Email">The address.</param>
/// <param name="Password">The password.</param>
public sealed record SignInCommand(string? Email, string? Password);

/// <summary>Signs out: moves the account's stamp, because a copied cookie outlives a deleted one.</summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
public sealed class SignOutHandler(IAccountStore? store) : ICommandHandler<SignOutCommand, bool>
{
    /// <summary>Ends every session of the signed-in account, if there is one here; true when there was one to end.</summary>
    /// <param name="command">The session's account, or null.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<bool> HandleAsync(SignOutCommand command, CancellationToken cancellation)
    {
        if (command.UserId is not null && store is not null && await store.FindByIdAsync(command.UserId, cancellation) is { } account)
        {
            await store.EndSessionsAsync(account, cancellation);
            return true;
        }
        return false;
    }
}

/// <summary>A sign-out.</summary>
/// <param name="UserId">The session's account, or null.</param>
public sealed record SignOutCommand(string? UserId);

/// <summary>The account the session belongs to on this store, or none: a session opened on the other store reads as signed out here.</summary>
/// <param name="store">The request's store's accounts, or null where it keeps none.</param>
public sealed class WhoAmIHandler(IAccountReader? store) : IQueryHandler<WhoAmIQuery, Account?>
{
    /// <summary>The account, or null.</summary>
    /// <param name="query">The session's account, or null.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Account?> HandleAsync(WhoAmIQuery query, CancellationToken cancellation) =>
        query.UserId is null || store is null ? null : await store.FindByIdAsync(query.UserId, cancellation);
}

/// <summary>Who the session belongs to on this store.</summary>
/// <param name="UserId">The session's account, or null.</param>
public sealed record WhoAmIQuery(string? UserId);
// #endregion session-handlers

/// <summary>What an account request came to.</summary>
public enum AccountAnswerKind
{
    /// <summary>It worked.</summary>
    Done,

    /// <summary>The request's store keeps no accounts.</summary>
    Unavailable,

    /// <summary>The request did not carry what it needs.</summary>
    Unreadable,

    /// <summary>The hour's allowance is spent.</summary>
    TooMany,

    /// <summary>Refused: a sign-in that did not match, or a registration with a problem.</summary>
    Refused,
}

/// <summary>An account request's answer.</summary>
/// <param name="Kind">What it came to.</param>
/// <param name="Account">The account, when it worked.</param>
/// <param name="Problem">What was wrong, for a refused registration.</param>
public sealed record AccountAnswer(AccountAnswerKind Kind, Account? Account, AccountProblem Problem)
{
    /// <summary>An answer with no account.</summary>
    /// <param name="kind">What it came to.</param>
    public static AccountAnswer Of(AccountAnswerKind kind) => new(kind, null, AccountProblem.None);
}
